// CropSpawner.cs
using UnityEngine;
using System.Collections.Generic;
using System.Collections;
using System.Linq; // Added for .Any() in InitializeCropTypeMap()

public class CropSpawner : MonoBehaviour
{
    public CropType[] availableCropTypes; // Assign all your CropType ScriptableObjects here
    public Transform cropParentContainer; // An empty GameObject to hold all spawned crops, for organization

    private Dictionary<LayerMask, CropType> layerToCropTypeMap;
    private Dictionary<GameObject, Coroutine> activeGrowthCoroutines = new Dictionary<GameObject, Coroutine>();

    // Basic object pooling for crops
    private Dictionary<GameObject, Queue<GameObject>> pooledCrops = new Dictionary<GameObject, Queue<GameObject>>();
    private Dictionary<GameObject, List<GameObject>> activeCrops = new Dictionary<GameObject, List<GameObject>>();


    void Awake()
    {
        InitializeCropTypeMap();
    }

    void Start()
    {
        // Initial spawn is handled here. If this component is copied at runtime,
        // LineMeshManager will call ForceSpawnCrops() after JSON overwrite.
        SpawnCropsBasedOnMainObjectLayer(); 
    }

    void OnDisable()
    {
        StopAllCropGrowth();
    }

    private void InitializeCropTypeMap()
    {
        // Only initialize if not already initialized or if there are new types to map
        if (layerToCropTypeMap == null)
        {
            layerToCropTypeMap = new Dictionary<LayerMask, CropType>();
        }
        
        // Clear existing map if we are re-initializing (e.g., from ForceSpawnCrops)
        if (layerToCropTypeMap.Any()) 
        {
            layerToCropTypeMap.Clear();
        }

        foreach (CropType cropType in availableCropTypes)
        {
            if (cropType == null)
            {
                Debug.LogWarning("CropSpawner: A null CropType was found in 'availableCropTypes' array. Skipping.");
                continue;
            }

            // LayerMask.value is the integer representation of the bitmask
            int layerValue = cropType.cropLayer.value;
            // We need to find the actual layer index (0-31) if we were to use LayerMask.LayerToName directly,
            // but for dictionary keying, the value itself is fine.
            // When checking against gameObject.layer, that is a single index, so we compare 1 << index.
            // The key in the map should be the full bitmask to match how LayerMask is used.
            if (!layerToCropTypeMap.ContainsKey(new LayerMask { value = layerValue }))
            {
                layerToCropTypeMap.Add(new LayerMask { value = layerValue }, cropType);
            }
            else
            {
                // Convert LayerMask value back to a name for the warning message
                // This is a bit tricky, as LayerMask.LayerToName expects an index.
                // We'll just use the numerical value for the warning.
                Debug.LogWarning($"CropSpawner: Duplicate CropType found for layer value {layerValue}. Only the first one will be used.");
            }
        }
    }

    private GameObject GetPooledCrop(GameObject prefab)
    {
        if (!pooledCrops.ContainsKey(prefab))
        {
            pooledCrops.Add(prefab, new Queue<GameObject>());
            activeCrops.Add(prefab, new List<GameObject>());
        }

        if (pooledCrops[prefab].Count > 0)
        {
            GameObject obj = pooledCrops[prefab].Dequeue();
            obj.SetActive(true);
            activeCrops[prefab].Add(obj);
            return obj;
        }
        else
        {
            GameObject newObj = Instantiate(prefab);
            if (cropParentContainer != null)
            {
                newObj.transform.SetParent(cropParentContainer);
            }
            activeCrops[prefab].Add(newObj);
            return newObj;
        }
    }

    private void ReturnPooledCrop(GameObject crop, GameObject prefab)
    {
        if (activeCrops.ContainsKey(prefab) && activeCrops[prefab].Contains(crop))
        {
            activeCrops[prefab].Remove(crop);
        }

        crop.SetActive(false);
        if (pooledCrops.ContainsKey(prefab))
        {
            pooledCrops[prefab].Enqueue(crop);
        }
        else
        {
            Destroy(crop);
        }

        if (activeGrowthCoroutines.ContainsKey(crop))
        {
            StopCoroutine(activeGrowthCoroutines[crop]);
            activeGrowthCoroutines.Remove(crop);
        }
    }

    // Public method to be called by LineMeshManager after copying this component
    public void ForceSpawnCrops()
    {
        // Re-initialize the map to ensure it uses the newly copied 'availableCropTypes' data.
        InitializeCropTypeMap(); 
        
        // Now, proceed with spawning based on the newly initialized map.
        // This ensures the CropSpawner correctly reads its data after being copied.
        SpawnCropsBasedOnMainObjectLayer();
        Debug.Log("CropSpawner: Force spawning crops due to external initialization request.");
    }


    private void SpawnCropsBasedOnMainObjectLayer() // Changed back to private as ForceSpawnCrops is the public entry
    {
        // Get the layer of the GameObject this script is attached to
        int mainObjectLayerIndex = gameObject.layer; // This is the single layer index (0-31)

        // Determine the CropType based on the main object's layer.
        // We need to compare the single layer index of the GameObject with the bitmask in CropType.cropLayer.
        CropType cropTypeToSpawn = null;
        foreach (var entry in layerToCropTypeMap)
        {
            // Check if the GameObject's layer index is present in the LayerMask bitmask
            if (((1 << mainObjectLayerIndex) & entry.Key.value) != 0)
            {
                cropTypeToSpawn = entry.Value;
                break;
            }
        }

        if (cropTypeToSpawn != null)
        {
            // If a CropType is found for the main object's layer, spawn all children with this type
            foreach (Transform child in transform)
            {
                GameObject spawnedCrop = GetPooledCrop(cropTypeToSpawn.cropModelPrefab);
                if (spawnedCrop != null)
                {
                    spawnedCrop.transform.position = child.position;
                    spawnedCrop.transform.rotation = child.rotation;
                    spawnedCrop.transform.localScale = Vector3.zero; // Start at zero scale for growth animation

                    // Start the growth process for this crop
                    Coroutine growthCoroutine = StartCoroutine(GrowCrop(spawnedCrop, cropTypeToSpawn.cropStages));
                    activeGrowthCoroutines.Add(spawnedCrop, growthCoroutine);
                }
                else
                {
                    Debug.LogWarning($"CropSpawner: Crop model prefab for CropType '{cropTypeToSpawn.name}' is null. Skipping spawning for child '{child.name}'.");
                }
            }
        }
        else
        {
            Debug.LogWarning($"CropSpawner: The main GameObject '{gameObject.name}' on layer '{LayerMask.LayerToName(mainObjectLayerIndex)}' does not have a corresponding CropType assigned in the CropSpawner's 'availableCropTypes' array or the layer is not correctly mapped. No crops will be spawned.");
        }
    }


    private IEnumerator GrowCrop(GameObject cropModel, CropStage[] stages)
    {
        if (cropModel == null) yield break; // Ensure cropModel is not null
        if (stages == null || stages.Length == 0)
        {
            Debug.LogWarning($"CropSpawner: No growth stages defined for {cropModel.name}. Skipping growth animation.");
            yield break;
        }

        Vector3 currentScale = cropModel.transform.localScale;

        foreach (CropStage stage in stages)
        {
            float timer = 0f;
            Vector3 startScale = currentScale;
            Vector3 targetScale = stage.targetScale;

            if (stage.scaleDuration <= 0) // Avoid division by zero or infinite loop for zero duration
            {
                cropModel.transform.localScale = targetScale;
                currentScale = targetScale;
                continue; // Move to next stage instantly
            }

            while (timer < stage.scaleDuration)
            {
                currentScale = Vector3.Lerp(startScale, targetScale, timer / stage.scaleDuration);
                cropModel.transform.localScale = currentScale;
                timer += Time.deltaTime;
                yield return null; // Wait for the next frame
            }

            // Ensure the final scale is exactly the target scale
            cropModel.transform.localScale = targetScale;
            currentScale = targetScale; // Update currentScale for the next stage's starting point
        }
    }

    public void StopAllCropGrowth()
    {
        foreach (var entry in activeGrowthCoroutines)
        {
            // Check if the coroutine is still running before stopping
            // (Stopping a null coroutine or one that already finished won't cause issues, but good practice)
            StopCoroutine(entry.Value);
        }
        activeGrowthCoroutines.Clear();
    }

    public void ClearAllSpawnedCrops()
    {
        StopAllCropGrowth();

        foreach (var entry in activeCrops)
        {
            GameObject prefab = entry.Key;
            List<GameObject> cropsToReturn = new List<GameObject>(entry.Value); // Create a copy to modify the original list
            foreach (GameObject crop in cropsToReturn)
            {
                if (crop != null) // Ensure the crop object still exists
                {
                    ReturnPooledCrop(crop, prefab);
                }
            }
            entry.Value.Clear();
        }
    }
}
