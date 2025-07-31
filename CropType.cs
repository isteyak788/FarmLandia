using UnityEngine;
using System; // Required for System.Serializable

[CreateAssetMenu(fileName = "NewCropType", menuName = "Farming/Crop Type")]
public class CropType : ScriptableObject
{
    public string cropName;
    public Sprite cropImage; // For UI display, if needed
    public GameObject cropModelPrefab; // The 3D model of the crop
    public LayerMask cropLayer; // The layer associated with this crop type

    public CropStage[] cropStages;
}

[Serializable] // Make this struct serializable so it can be viewed/edited in the Inspector
public struct CropStage
{
    public Vector3 targetScale; // The scale at this stage
    public float scaleDuration; // How long it takes to reach this scale from the previous one (in seconds)
}
