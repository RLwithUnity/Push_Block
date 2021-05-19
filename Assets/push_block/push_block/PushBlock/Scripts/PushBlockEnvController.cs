using System.Collections;
using System.Collections.Generic;
using Unity.MLAgents;
using UnityEngine;
using UnityEngine.UI;

public class PushBlockEnvController : MonoBehaviour
{
    [System.Serializable]
    public class PlayerInfo
    {
        public PushAgentCollab Agent;
        [HideInInspector]
        public Vector3 StartingPos;
        [HideInInspector]
        public Quaternion StartingRot;
        [HideInInspector]
        public Rigidbody Rb;
    }

    [System.Serializable]
    public class BlockInfo
    {
        public Transform T;
        [HideInInspector]
        public Vector3 StartingPos;
        [HideInInspector]
        public Quaternion StartingRot;
        [HideInInspector]
        public Rigidbody Rb;
    }

    /// <summary>
    /// Max Academy steps before this platform resets
    /// </summary>
    /// <returns></returns>
    [Header("Max Environment Steps")] public int MaxEnvironmentSteps = 25000;

    /// <summary>
    /// The area bounds.
    /// </summary>
    [HideInInspector]
    public Bounds areaBounds;
    public Bounds goalBounds; // 수정한 부분
    /// <summary>
    /// The ground. The bounds are used to spawn the elements.
    /// </summary>
    public GameObject ground;
    public GameObject area;
    public GameObject goal; // 수정한 부분

    Material m_GroundMaterial; //cached on Awake()

    /// <summary>
    /// We will be changing the ground material based on success/failue
    /// </summary>
    Renderer m_GroundRenderer;

    //List of Agents On Platform
    public List<PlayerInfo> AgentsList = new List<PlayerInfo>();
    //List of Blocks On Platform
    public List<BlockInfo> BlocksList = new List<BlockInfo>();


    public bool UseRandomAgentRotation = true;
    public bool UseRandomAgentPosition = true;
    public bool UseRandomBlockRotation = true;
    public bool UseRandomBlockPosition = true;
    private PushBlockSettings m_PushBlockSettings;

    private int m_NumberOfRemainingBlocks;

    private SimpleMultiAgentGroup m_AgentGroup;

    private int m_ResetTimer;

    // 수정한 부분
    private int m_MaxGoalInAgentNo = 0;
    private float GROUND_WIDTH = 0;
    private float GROUND_HEIGHT = 0;
    private float MAX_GROUND_DISTNACE = 0;
    private List<BlockInfo> m_TargetBlockList = new List<BlockInfo>();
    private bool bProximityReward = true;
    // 수정한 부분

    void Start()
    {

        // Get the ground's bounds
        areaBounds = ground.GetComponent<Collider>().bounds;

        // Get the goal's bounds (수정한 부분)
        goalBounds = goal.GetComponent<Collider>().bounds;
        GROUND_WIDTH = areaBounds.extents.x * 2.0f;
        GROUND_HEIGHT = areaBounds.extents.z * 2.0f;
        MAX_GROUND_DISTNACE = Mathf.Sqrt(GROUND_WIDTH * GROUND_WIDTH + GROUND_HEIGHT * GROUND_HEIGHT);
        // Debug.Log("MAX_GROUND_DISTNACE = " + (MAX_GROUND_DISTNACE).ToString());

        // Get the ground renderer so we can change the material when a goal is scored
        m_GroundRenderer = ground.GetComponent<Renderer>();
        // Starting material
        m_GroundMaterial = m_GroundRenderer.material;
        m_PushBlockSettings = FindObjectOfType<PushBlockSettings>();
        // Initialize Blocks
        foreach (var item in BlocksList)
        {
            item.StartingPos = item.T.transform.position;
            item.StartingRot = item.T.transform.rotation;
            item.Rb = item.T.GetComponent<Rigidbody>();
        }
        // Initialize TeamManager
        m_AgentGroup = new SimpleMultiAgentGroup();
        foreach (var item in AgentsList)
        {
            item.StartingPos = item.Agent.transform.position;
            item.StartingRot = item.Agent.transform.rotation;
            item.Rb = item.Agent.GetComponent<Rigidbody>();
            m_AgentGroup.RegisterAgent(item.Agent);
        }
        ResetScene();
    }

    void FixedUpdate()
    {
        m_ResetTimer += 1;
        if (m_ResetTimer >= MaxEnvironmentSteps && MaxEnvironmentSteps > 0)
        {
            m_AgentGroup.GroupEpisodeInterrupted();
            ResetScene();
        }
        //Hurry Up Penalty
        m_AgentGroup.AddGroupReward(-0.5f / MaxEnvironmentSteps);

        if (bProximityReward)
        {
            //Agent가 Target Block에 가까울수록 높은 Reward를 줌
            AddBlockProximityReward();

            //TargetBlock이 Goal과 가까울수록 높은 Reward를 줌
            AddGoalProximityReward();
        }
    }

    /// <summary>
    /// Agent가 Target Block에 가까울수록 높은 Reward를 줌
    /// </summary>
    void AddBlockProximityReward()
    {
        if (m_NumberOfRemainingBlocks == 0) return;

        foreach (var agent in AgentsList)
        {
            float min_distance = MAX_GROUND_DISTNACE;
            foreach (var TargetBlock in m_TargetBlockList)
            {
                float current_dist = Vector3.Distance(agent.Agent.transform.position, TargetBlock.T.transform.position);
                // Debug.Log("AddBlockProximityReward = " + (current_dist).ToString());
                if (current_dist < min_distance) min_distance = current_dist;
            }

            float ProximityReward = -min_distance / MAX_GROUND_DISTNACE / MaxEnvironmentSteps;
            // float ProximityReward = -min_distance / MAX_GROUND_DISTNACE;
            m_AgentGroup.AddGroupReward(ProximityReward);

            // Debug.Log("AddBlockProximityReward = " + (ProximityReward).ToString());
        }
    }


    /// <summary>
    /// Block이 Goal에 가까이 있으면 Reward를 추가
    /// </summary>
    void AddGoalProximityReward()
    {
        if (m_NumberOfRemainingBlocks == 0) return;

        Collider goalCollider = goal.GetComponent<Collider>();
        foreach (var TargetBlock in m_TargetBlockList)
        {
            Vector3 goal_position = goalCollider.ClosestPoint(TargetBlock.T.transform.position);
            float goal_distance = Vector3.Distance(goal_position, TargetBlock.T.transform.position);
            float ProximityReward = -goal_distance / MAX_GROUND_DISTNACE / MaxEnvironmentSteps;
            // float ProximityReward = -goal_distance/MAX_GROUND_DISTNACE;
            m_AgentGroup.AddGroupReward(ProximityReward);

            // Debug.Log("AddGoalProximityReward = " + (ProximityReward).ToString());
        }
    }

    /// <summary>
    /// TargetBlock이 Goal과 가까울수록 높은 Reward를 줌
    /// </summary>
    List<BlockInfo> GetTargetBlockList()
    {
        if (m_NumberOfRemainingBlocks == 0) return null;

        // 1. 다음 순서에 해당하는 번호 계산
        int TargetNo = int.MaxValue;
        foreach (var block in BlocksList)
        {
            Text UIText = block.T.GetComponentInChildren<Text>();
            int CurrentAgentNo = int.Parse(UIText.text);
            if (block.T.gameObject.activeSelf && m_MaxGoalInAgentNo <= CurrentAgentNo)
                if (TargetNo == int.MaxValue) TargetNo = CurrentAgentNo;
                else if (CurrentAgentNo <= TargetNo) TargetNo = CurrentAgentNo;
        }

        // 2. 다음 순서에 해당하는 번호를 가진 블록 리스트 구성
        List<BlockInfo> TargetBlockList = new List<BlockInfo>();
        foreach (var block in BlocksList)
        {
            Text UIText = block.T.GetComponentInChildren<Text>();
            int CurrentAgentNo = int.Parse(UIText.text);
            if (block.T.gameObject.activeSelf && CurrentAgentNo == TargetNo)
            {
                TargetBlockList.Add(block);
                Debug.Log("GetTargetBlock = " + (TargetNo).ToString());
            }
        }
        return TargetBlockList;
    }

    /// <summary>
    /// Use the ground's bounds to pick a random spawn position.
    /// </summary>
    public Vector3 GetRandomSpawnPos()
    {
        var foundNewSpawnLocation = false;
        var randomSpawnPos = Vector3.zero;
        while (foundNewSpawnLocation == false)
        {
            var randomPosX = Random.Range(-areaBounds.extents.x * m_PushBlockSettings.spawnAreaMarginMultiplier,
                areaBounds.extents.x * m_PushBlockSettings.spawnAreaMarginMultiplier);

            var randomPosZ = Random.Range(-areaBounds.extents.z * m_PushBlockSettings.spawnAreaMarginMultiplier,
                areaBounds.extents.z * m_PushBlockSettings.spawnAreaMarginMultiplier);
            randomSpawnPos = ground.transform.position + new Vector3(randomPosX, 1f, randomPosZ);
            if (Physics.CheckBox(randomSpawnPos, new Vector3(1.5f, 0.01f, 1.5f)) == false)
            {
                foundNewSpawnLocation = true;
            }
        }
        return randomSpawnPos;
    }

    /// <summary>
    /// Resets the block position and velocities.
    /// </summary>
    void ResetBlock(BlockInfo block)
    {
        // Get a random position for the block.
        block.T.position = GetRandomSpawnPos();

        // Reset block velocity back to zero.
        block.Rb.velocity = Vector3.zero;

        // Reset block angularVelocity back to zero.
        block.Rb.angularVelocity = Vector3.zero;
    }

    /// <summary>
    /// Swap ground material, wait time seconds, then swap back to the regular material.
    /// </summary>
    IEnumerator GoalScoredSwapGroundMaterial(Material mat, float time)
    {
        m_GroundRenderer.material = mat;
        yield return new WaitForSeconds(time); // Wait for 2 sec
        m_GroundRenderer.material = m_GroundMaterial;
    }

    /// <summary>
    /// Called when the agent moves the block into the goal.
    /// </summary>
    public void ScoredAGoal(Collider col, float score)
    {
        print($"Scored {score} on {gameObject.name}");

        //Decrement the counter
        m_NumberOfRemainingBlocks--;

        //Are we done?
        bool done = m_NumberOfRemainingBlocks == 0;

        //Disable the block
        col.gameObject.SetActive(false);

        //Give Agent Rewards (수정 부분)
        Text UIText = col.gameObject.GetComponentInChildren<Text>();
        // Debug.Log("Text Number = " + UIText.text);

        int CurrentAgentNo = int.Parse(UIText.text);
        if (CurrentAgentNo < m_MaxGoalInAgentNo)
            score = -score;
        else
            m_MaxGoalInAgentNo = CurrentAgentNo;

        m_AgentGroup.AddGroupReward(score);

        // 수정한 부분
        if (bProximityReward)
        {
            m_TargetBlockList = GetTargetBlockList();
        }

        // Swap ground material for a bit to indicate we scored.
        StartCoroutine(GoalScoredSwapGroundMaterial(m_PushBlockSettings.goalScoredMaterial, 0.5f));

        if (done)
        {
            //Reset assets
            m_AgentGroup.EndGroupEpisode();
            ResetScene();
        }
    }

    Quaternion GetRandomRot()
    {
        return Quaternion.Euler(0, Random.Range(0.0f, 360.0f), 0);
    }

    public void ResetScene()
    {
        m_ResetTimer = 0;

        //Random platform rotation
        var rotation = Random.Range(0, 4);
        var rotationAngle = rotation * 90f;
        area.transform.Rotate(new Vector3(0f, rotationAngle, 0f));

        //Reset Agents
        foreach (var item in AgentsList)
        {
            var pos = UseRandomAgentPosition ? GetRandomSpawnPos() : item.StartingPos;
            var rot = UseRandomAgentRotation ? GetRandomRot() : item.StartingRot;

            item.Agent.transform.SetPositionAndRotation(pos, rot);
            item.Rb.velocity = Vector3.zero;
            item.Rb.angularVelocity = Vector3.zero;
        }

        //Reset Blocks
        foreach (var item in BlocksList)
        {
            var pos = UseRandomBlockPosition ? GetRandomSpawnPos() : item.StartingPos;
            var rot = UseRandomBlockRotation ? GetRandomRot() : item.StartingRot;

            item.T.transform.SetPositionAndRotation(pos, rot);
            item.Rb.velocity = Vector3.zero;
            item.Rb.angularVelocity = Vector3.zero;
            item.T.gameObject.SetActive(true);
        }

        //Reset counter
        m_NumberOfRemainingBlocks = BlocksList.Count;

        if (bProximityReward)
        {
            // 수정한 부분
            m_MaxGoalInAgentNo = 0;
            m_TargetBlockList = GetTargetBlockList();
        }
    }
}
