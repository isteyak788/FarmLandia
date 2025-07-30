using UnityEngine;

public class CropSpawner : MonoBehaviour
{
    public GameObject cropPrefab; // Assign your crop prefab in the Inspector

    void Start()
    {
        SpawnCrops();
    }

    void SpawnCrops()
    {
        // Get all child transforms of the object this script is attached to
        int childCount = transform.childCount;

        for (int i = 0; i < childCount; i++)
        {
            Transform childTransform = transform.GetChild(i);

            // Spawn the crop prefab at the child's position and rotation
            if (cropPrefab != null)
            {
                Instantiate(cropPrefab, childTransform.position, childTransform.rotation);
                Debug.Log($"Spawned {cropPrefab.name} at position: {childTransform.position}");
            }
            else
            {
                Debug.LogWarning("Crop Prefab is not assigned in the Inspector!");
                break; // Stop if the prefab isn't set
            }
        }

        if (childCount == 0)
        {
            Debug.LogWarning("No child objects found to use as spawn points.");
        }
    }
}
