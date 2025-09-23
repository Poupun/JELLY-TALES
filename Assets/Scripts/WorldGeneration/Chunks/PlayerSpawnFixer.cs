using UnityEngine;

namespace WorldGeneration.Chunks
{
    /// <summary>
    /// Ensures player spawns properly after chunk optimizations
    /// </summary>
    public class PlayerSpawnFixer : MonoBehaviour
    {
        [Header("Player Spawn Fix")]
        [Tooltip("Force enable player immediately on start")]
        public bool forceEnablePlayer = true;
        
        [Tooltip("Player tag to search for")]
        public string playerTag = "Player";
        
        [Tooltip("Delay before enabling player (in seconds)")]
        public float enableDelay = 0.1f;
        
        private void Start()
        {
            if (forceEnablePlayer)
            {
                StartCoroutine(EnablePlayerAfterDelay());
            }
        }
        
        private System.Collections.IEnumerator EnablePlayerAfterDelay()
        {
            yield return new WaitForSeconds(enableDelay);
            
            GameObject player = GameObject.FindGameObjectWithTag(playerTag);
            if (player != null)
            {
                if (!player.activeInHierarchy)
                {
                    player.SetActive(true);
                    Debug.Log("PlayerSpawnFixer: Enabled player GameObject");
                }
                
                // Ensure player components are enabled
                var playerController = player.GetComponent<FirstPersonController>();
                if (playerController != null && !playerController.enabled)
                {
                    playerController.enabled = true;
                    Debug.Log("PlayerSpawnFixer: Enabled FirstPersonController");
                }
                
                var playerMovement = player.GetComponent<PlayerController>();
                if (playerMovement != null && !playerMovement.enabled)
                {
                    playerMovement.enabled = true;
                    Debug.Log("PlayerSpawnFixer: Enabled PlayerController");
                }
            }
            else
            {
                Debug.LogWarning("PlayerSpawnFixer: Could not find player GameObject with tag '" + playerTag + "'");
                
                // Try alternative search methods
                player = FindObjectOfType<FirstPersonController>()?.gameObject;
                if (player == null)
                {
                    player = FindObjectOfType<PlayerController>()?.gameObject;
                }
                
                if (player != null)
                {
                    if (!player.activeInHierarchy)
                    {
                        player.SetActive(true);
                        Debug.Log("PlayerSpawnFixer: Found and enabled player via component search");
                    }
                }
                else
                {
                    Debug.LogError("PlayerSpawnFixer: Could not find player GameObject at all!");
                }
            }
        }
        
        private void Update()
        {
            // Continuously check if player is missing and try to fix it
            if (Time.frameCount % 60 == 0) // Check every 60 frames (about once per second)
            {
                GameObject player = GameObject.FindGameObjectWithTag(playerTag);
                if (player == null || !player.activeInHierarchy)
                {
                    Debug.LogWarning("PlayerSpawnFixer: Player missing or inactive, attempting fix...");
                    StartCoroutine(EnablePlayerAfterDelay());
                }
            }
        }
    }
}