using UnityEngine;


public class DynamicSpawner : MonoBehaviour
{
    [Header("Prefab Settings")][SerializeField] private GameObject prefab1;
    [Header("Prefab Settings")][SerializeField] private GameObject prefab2;

    [Header("Tag Settings")][SerializeField] private string targetTag = "Target"; // Objects must have this tag

    [Header("Offset Settings")][SerializeField] private float verticalOffset = 1.0f;

    void Start()
    {
        SpawnOnTargetLocations();
    }

 

    public void SpawnOnTargetLocations()
    {
        
        
        if (prefab1 == null || prefab2 == null)
        {
            Debug.LogError("No prefab assigned to spawn!");
            return;
        }

        //Automatically find all objects in the scene with the specific tag
        GameObject[] targets = GameObject.FindGameObjectsWithTag(targetTag);

        if (targets.Length == 0)
        {
            Debug.LogWarning($"No objects found with the tag: '{targetTag}'");
            return;
        }

        //Loop through each found object and read its location
        foreach (GameObject target in targets)
        {

            // 1. Pick a random number: 0 or 1 (2 is exclusive) |Coult possibly use this to determine future randomized features
            // This number can even be increased in the future depending on nunmber or prefabs/items
            int rand = Random.Range(0, 2);

            // 2. Choose the prefab based on the random number 
            GameObject chosenPrefab = (rand == 0) ? prefab1 : prefab2;

            //Get the exact X, Y, Z coordinates of the target object
            Vector3 targetLocation = target.transform.position;

            //Add the vertical offset to the Y coordinate so it spawns on top
            Vector3 spawnPosition = new Vector3(targetLocation.x, targetLocation.y + verticalOffset, targetLocation.z);

            //Spawn the prefab at that exact location
            Instantiate(chosenPrefab, spawnPosition, Quaternion.identity);
        }
    }
}
