// Assets/Scripts/CORE/farming/CropType.cs
using UnityEngine;
using System; // Required for [Serializable]

[CreateAssetMenu(fileName = "NewCropType", menuName = "Farming/Crop Type")]
public class CropType : ScriptableObject
{
    [Header("Basic Crop Info")]
    public string cropName = "New Crop";
    public Sprite cropIcon; // For UI display on buttons and panels

    [Header("Growth Parameters")]
    [Tooltip("The total number of in-game days it takes for this crop to fully grow and be harvestable.")]
    public float timeToGrowInDays = 10f; // Example: 10 days to fully grow

    // Optional: Define seasons if your game has them
    public enum Season
    {
        Spring, Summer, Autumn, Winter, All
    }
    public Season idealSeason = Season.All;

    // FIX: Add IsSingleHarvest property, as previously discussed.
    [Header("Harvesting")]
    [Tooltip("True if the crop is destroyed after harvest (e.g., wheat), false if it regrows (e.g., berries).")]
    public bool IsSingleHarvest = false;

    [Header("Growth Models")]
    [Tooltip("The 3D model to display immediately after planting.")]
    public GameObject plantedModel;
    
    [Tooltip("Models that display *before* the first defined growth stage (e.g., small sprouts).")]
    public GameObject[] preGrowthModels;

    [Tooltip("An array defining different visual stages of growth with their progress thresholds and durations.")]
    public GrowthStage[] growthStages; // Array of GrowthStage objects
    
    [Tooltip("Models that display *after* the final growth stage but before harvesting (e.g., withered plants).")]
    public GameObject[] postGrowthModels;

    [Tooltip("The 3D model to display once the crop has been harvested (e.g., stubble, bare ground).")]
    public GameObject harvestedModel;

    /// <summary>
    /// Represents a specific visual stage of the crop's growth.
    /// </summary>
    [Serializable] // Make this struct/class serializable so it shows in the Inspector
    public class GrowthStage
    {
        [Tooltip("The normalized growth progress (0 to 1) at which this stage's visual becomes active.")]
        [Range(0f, 1f)] // Ensure threshold is between 0 and 1
        public float threshold = 0.2f; // E.g., at 20% of total growth days, show this model

        [Tooltip("The duration (in in-game days) for which this visual stage should be displayed after its threshold is met.")]
        public float timeToCompleteInDays = 1f; // How long this visual stage should last

        // FIX: Renamed 'stagePrefab' to 'model' to match usage in CropManager
        [Tooltip("The 3D model associated with this growth stage.")]
        public GameObject model;
    }
}
