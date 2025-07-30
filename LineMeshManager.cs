using System.Collections.Generic;
using UnityEngine;
using System.Linq; // For .OrderBy and .FirstOrDefault
using UnityEngine.EventSystems; // Required for UI checks
using UnityEngine.UI; // REQUIRED for Button type
using System; // Required for Type

// This enum defines the available curve types.
public enum CurveType
{
    NoCurve,
    CatmullRom,
    FilletCorners
}

// Mark this class as Serializable so it can be shown and edited in the Unity Inspector.
[System.Serializable]
public class MeshConfiguration
{
    [Header("Activation Settings for this Config")]
    [Tooltip("Drag the UI Buttons here that should activate point placement using THIS specific configuration.")]
    public Button[] activateDrawingButtons;

    [Header("Mesh Creation Settings")]
    [Tooltip("Prefab to instantiate at each placed point (optional, for visual feedback).")]
    public GameObject pointPrefab;
    [Tooltip("Material for the generated mesh. IMPORTANT: Assign a visible material here.")]
    public Material meshMaterial;
    [Tooltip("Material for the LineRenderer drawing the outline (for the spawner's preview line).")]
    public Material lineMaterial;
    [Tooltip("Width of the line drawn by the LineRenderer.")]
    public float lineWidth = 0.1f;

    [Header("Curve Settings")]
    [Tooltip("Select the type of curve to use for smoothing the line.")]
    public CurveType curveType = CurveType.CatmullRom;
    [Tooltip("Number of segments per curve section. Higher values make the curve smoother.")]
    [Range(2, 20)]
    public int curveSegments = 10;
    [Tooltip("For FilletCorners, this controls the radius/amount of the corner curve (0.0 means sharp corners, 1.0 means large fillets).")]
    [Range(0.0f, 0.49f)]
    public float filletAmount = 0.1f;

    [Header("Normal Settings")]
    [Tooltip("If true, the mesh normals will be inverted (making the inside surface visible from the outside).")]
    public bool invertMesh = false;

    [Header("Ground Conformance")]
    [Tooltip("The LayerMask for the ground object(s) the mesh/line should conform to.")]
    public LayerMask groundLayer;
    [Tooltip("Offset above the ground where the line/mesh vertices will be placed.")]
    public float groundOffset = 0.05f;

    [Header("Mesh Generation Details")]
    [Tooltip("Determines how far the first inner loop of vertices is from the outer spline. 0 = collapses to centroid, 1 = same as outer loop.")]
    [Range(0.0f, 0.9f)]
    public float innerLoopScale = 0.5f;
    [Tooltip("Determines how far the second (innermost) loop of vertices is from the outer spline. Must be less than Inner Loop Scale.")]
    [Range(0.0f, 0.89f)]
    public float innermostLoopScale = 0.25f;

    [Tooltip("If true, the MeshCollider will be marked as convex. Convex MeshColliders can interact with other convex colliders.")]
    public bool makeConvex = false;

    [Header("Fill Objects")]
    [Tooltip("Prefab to instantiate inside the generated mesh in a grid pattern.")]
    public GameObject fillObjectPrefab;
    [Tooltip("Spacing between fill objects in the grid.")]
    public float fillObjectSpacing = 1f;
    [Tooltip("Offset for fill objects in the Y-axis relative to the ground.")]
    public float fillOffset = 0f;

    [Header("Dynamic Components")]
    [Tooltip("Drag and drop components (e.g., Rigidbody, Collider, your custom scripts) from other GameObjects here. Their type and *serializable* data will be copied to the generated mesh. Note: Only serializable fields will be copied.")]
    public Component[] componentsToCopy;

    [Header("Generated Mesh Layer")]
    [Tooltip("The name of the layer to assign to the newly generated mesh GameObject.")]
    public string generatedMeshLayerName = "Default";

    // OnValidate for this serializable class (used by its parent MonoBehaviour's OnValidate)
    public void Validate()
    {
        if (innermostLoopScale >= innerLoopScale)
        {
            innermostLoopScale = innerLoopScale - 0.01f;
            if (innermostLoopScale < 0) innermostLoopScale = 0;
            Debug.LogWarning("Innermost Loop Scale must be less than Inner Loop Scale. Adjusted automatically for current config.");
        }
        if (fillObjectSpacing <= 0)
        {
            fillObjectSpacing = 0.1f;
            Debug.LogWarning("Fill Object Spacing must be greater than 0. Adjusted to 0.1 for current config.");
        }
        if (filletAmount < 0)
        {
            filletAmount = 0;
            Debug.LogWarning("Fillet Amount cannot be negative. Adjusted to 0 for current config.");
        }
        if (filletAmount >= 0.5f)
        {
            filletAmount = 0.49f;
            Debug.LogWarning("Fillet Amount capped at 0.49 to prevent potential overlaps for current config.");
        }
    }
}


[RequireComponent(typeof(LineRenderer))]
public class LineMeshManager : MonoBehaviour
{
    [Header("Manager Settings")]
    [Tooltip("Check this box ONLY for the LineMeshManager GameObject you place directly in the scene. This instance will manage spawning new meshes.")]
    public bool isSpawner = false;

    [Header("Configuration Selection")]
    [Tooltip("Define multiple mesh configurations here. Each element in this array is an independent set of settings.")]
    public MeshConfiguration[] meshConfigs;

    // Internal state for each LineMeshManager instance
    private List<Vector3> controlPoints = new List<Vector3>();
    private List<GameObject> pointVisuals = new List<GameObject>();
    private LineRenderer lineRenderer;

    private bool meshFinalizedForThisInstance = false;
    private bool initializedComponents = false;

    private static LineMeshManager currentDrawingInstance;
    private static MeshConfiguration activeMeshConfig; // Store the currently active config
    private static bool isDrawingSessionActive = false;

    private GameObject finalizedMeshGameObject;

    // Get the currently active configuration
    private MeshConfiguration GetActiveConfig()
    {
        if (activeMeshConfig == null)
        {
            Debug.LogError("No active Mesh Configuration set. Please activate a drawing session via a UI button configured for a specific Mesh Configuration.");
            return null;
        }
        return activeMeshConfig;
    }


    void Awake()
    {
        InitializeComponents();
    }

    void OnEnable()
    {
        if (isSpawner && meshConfigs != null)
        {
            foreach (MeshConfiguration config in meshConfigs)
            {
                if (config != null && config.activateDrawingButtons != null)
                {
                    foreach (Button button in config.activateDrawingButtons)
                    {
                        if (button != null)
                        {
                            // Use a lambda to capture the specific config for the button click
                            button.onClick.AddListener(() => StartNewDrawingSessionFromButton(config));
                            Debug.Log($"Subscribed to '{button.name}' click event for config ({(config.GetType().Name)}).");
                        }
                    }
                }
            }
        }
    }

    void OnDisable()
    {
        if (isSpawner && meshConfigs != null)
        {
            foreach (MeshConfiguration config in meshConfigs)
            {
                if (config != null && config.activateDrawingButtons != null)
                {
                    foreach (Button button in config.activateDrawingButtons)
                    {
                        if (button != null)
                        {
                            // For simplicity, here we just remove all listeners which is safe on disable.
                            button.onClick.RemoveAllListeners();
                            Debug.Log($"Unsubscribed all listeners from '{button.name}'.");
                        }
                    }
                }
            }
        }
    }

    void OnValidate()
    {
        // Call OnValidate for each MeshConfiguration to apply its internal validations
        if (meshConfigs != null)
        {
            foreach (MeshConfiguration config in meshConfigs)
            {
                if (config != null)
                {
                    config.Validate();
                }
            }
        }
    }

    private void InitializeComponents()
    {
        if (initializedComponents) return;

        lineRenderer = GetComponent<LineRenderer>();

        // When initializing, there might not be an active config yet.
        // LineRenderer properties will be set when a session is activated.
        lineRenderer.useWorldSpace = true;
        lineRenderer.enabled = true; // Start enabled for potential preview line
        initializedComponents = true;
    }


    void Update()
    {
        // Update LineRenderer properties if a config is active
        if (initializedComponents && activeMeshConfig != null) // Use activeMeshConfig
        {
            MeshConfiguration config = activeMeshConfig; // Use activeMeshConfig
            if (lineRenderer.startWidth != config.lineWidth)
            {
                lineRenderer.startWidth = config.lineWidth;
                lineRenderer.endWidth = config.lineWidth;
            }
            if (lineRenderer.material != config.lineMaterial)
            {
                lineRenderer.material = config.lineMaterial != null ? config.lineMaterial : new Material(Shader.Find("Sprites/Default"));
            }
        }


        // Prevent interaction when UI is clicked
        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
        {
            return;
        }

        if (isSpawner)
        {
            if (isDrawingSessionActive)
            {
                if (Input.GetMouseButtonDown(0)) // Left mouse button click
                {
                    MeshConfiguration config = GetActiveConfig();
                    if (config == null)
                    {
                        Debug.LogWarning("No active MeshConfig to draw with. Please activate a drawing session via a UI button.");
                        return;
                    }

                    Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
                    RaycastHit hit;

                    if (Physics.Raycast(ray, out hit, Mathf.Infinity, config.groundLayer))
                    {
                        Vector3 newPoint = hit.point + Vector3.up * config.groundOffset;

                        // Ensure this spawner instance is the active drawing instance
                        if (currentDrawingInstance == null || currentDrawingInstance.meshFinalizedForThisInstance)
                        {
                            currentDrawingInstance = this; // The spawner itself is the drawing instance
                            currentDrawingInstance.ResetDrawingState();
                            currentDrawingInstance.AddPoint(newPoint);
                            Debug.Log("Spawner is now the active drawing instance. First point added.");
                        }
                        else
                        {
                            currentDrawingInstance.AddPoint(newPoint);
                        }
                    }
                    else
                    {
                        Debug.LogWarning("Mouse click did not hit any object on the specified Ground Layer!");
                    }
                }
            }

            if (Input.GetKeyDown(KeyCode.R)) // Reset drawing with 'R' key
            {
                if (currentDrawingInstance != null && !currentDrawingInstance.meshFinalizedForThisInstance)
                {
                    currentDrawingInstance.ResetDrawingState();
                    isDrawingSessionActive = false;
                    activeMeshConfig = null; // Clear active config
                    currentDrawingInstance = null;
                    Debug.Log("Spawner: Current drawing reset.");
                }
                else if (currentDrawingInstance != null && currentDrawingInstance.meshFinalizedForThisInstance)
                {
                    currentDrawingInstance = null;
                    isDrawingSessionActive = false;
                    activeMeshConfig = null; // Clear active config
                    Debug.Log("Spawner: Cleared reference to last finalized mesh. Ready to start new drawing.");
                }
                else if (currentDrawingInstance == null)
                {
                    isDrawingSessionActive = false;
                    activeMeshConfig = null; // Clear active config
                    Debug.Log("Spawner: No active drawing to reset. Ready to start new drawing.");
                }
            }
        }

        // Only the currently active drawing instance should respond to Enter/Right-Click
        if (currentDrawingInstance == this && isDrawingSessionActive)
        {
            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)) // Finalize mesh with Enter key
            {
                if (controlPoints.Count >= 3)
                {
                    GenerateMesh();
                    meshFinalizedForThisInstance = true;
                    lineRenderer.enabled = false;

                    // Reset spawner for next drawing
                    ResetDrawingState(); // This resets the spawner's internal lists, not the generated mesh.
                    currentDrawingInstance = null;
                    isDrawingSessionActive = false;
                    activeMeshConfig = null; // Clear active config after finalizing
                    Debug.Log(gameObject.name + " mesh finalized and spawner reset. Ready for next mesh.");
                }
                else
                {
                    Debug.LogWarning("Need at least 3 points to form a polygon to generate a mesh. Current control points: " + controlPoints.Count);
                }
            }

            if (Input.GetMouseButtonDown(1)) // Right mouse button click to delete last point
            {
                DeleteLastPoint();
            }
        }
    }

    /// <summary>
    /// Called by UI Buttons to activate a new drawing session for the spawner with a specific configuration.
    /// </summary>
    public void StartNewDrawingSessionFromButton(MeshConfiguration configToActivate)
    {
        if (isSpawner)
        {
            if (configToActivate == null)
            {
                Debug.LogError("Attempted to activate drawing with a null MeshConfiguration!");
                return;
            }

            if (currentDrawingInstance == null || currentDrawingInstance.meshFinalizedForThisInstance)
            {
                isDrawingSessionActive = true;
                currentDrawingInstance = this;
                activeMeshConfig = configToActivate; // Set the active config
                ResetDrawingState(); // Reset state for a new drawing
                Debug.Log($"Drawing session activated with config: '{configToActivate.GetType().Name}'. Click on the ground to place the first point.");
            }
            else
            {
                Debug.LogWarning("Cannot start a new drawing session. An existing drawing is in progress. Please finalize or reset the current drawing.");
            }
        }
        else
        {
            Debug.LogError("This LineMeshManager instance is not marked as the spawner. The button should call the spawner's method.");
        }
    }

    /// <summary>
    /// Adds a new control point to the current drawing.
    /// </summary>
    /// <param name="point">The world position of the new point.</param>
    public void AddPoint(Vector3 point)
    {
        if (meshFinalizedForThisInstance) return; // Prevent adding points after mesh is finalized

        MeshConfiguration config = GetActiveConfig();
        if (config == null) return; // Should not happen if session is active

        controlPoints.Add(point);
        if (config.pointPrefab != null) // Use config's point prefab
        {
            GameObject visual = Instantiate(config.pointPrefab, point, Quaternion.identity, transform);
            pointVisuals.Add(visual);
        }
        UpdateLineRenderer();
    }

    /// <summary>
    /// Resets the drawing state of this LineMeshManager instance, clearing points and visuals.
    /// </summary>
    public void ResetDrawingState()
    {
        controlPoints.Clear();
        foreach (GameObject visual in pointVisuals)
        {
            Destroy(visual);
        }
        pointVisuals.Clear();

        lineRenderer.positionCount = 0;
        lineRenderer.enabled = true; // Re-enable for new drawing

        meshFinalizedForThisInstance = false;
        Debug.Log(gameObject.name + " drawing reset.");

        finalizedMeshGameObject = null; // Clear reference to old generated mesh
    }

    /// <summary>
    /// Deletes the last added control point.
    /// </summary>
    private void DeleteLastPoint()
    {
        if (controlPoints.Count > 0)
        {
            controlPoints.RemoveAt(controlPoints.Count - 1);
            if (pointVisuals.Count > 0)
            {
                Destroy(pointVisuals[pointVisuals.Count - 1]);
                pointVisuals.RemoveAt(pointVisuals.Count - 1);
            }
            UpdateLineRenderer();
            Debug.Log("Last point deleted. Current control points: " + controlPoints.Count);
        }
        else
        {
            Debug.Log("No points to delete.");
        }
    }

    /// <summary>
    /// Updates the LineRenderer to draw the spline based on current control points.
    /// </summary>
    void UpdateLineRenderer()
    {
        MeshConfiguration config = GetActiveConfig();
        if (config == null) return;

        if (controlPoints.Count < 2)
        {
            lineRenderer.positionCount = controlPoints.Count;
            if (controlPoints.Count == 1)
            {
                lineRenderer.SetPosition(0, controlPoints[0]);
            }
            return;
        }

        List<Vector3> splinePoints = GenerateSplinePoints(true, config); // Pass config

        lineRenderer.positionCount = splinePoints.Count;
        lineRenderer.SetPositions(splinePoints.ToArray());
    }

    /// <summary>
    /// Generates smoothed spline points using the selected interpolation algorithm.
    /// </summary>
    /// <param name="closedLoop">If true, the spline will form a closed loop.</param>
    /// <returns>A list of Vector3 points representing the spline.</returns>
    private List<Vector3> GenerateSplinePoints(bool closedLoop, MeshConfiguration config) // Added config parameter
    {
        List<Vector3> splinePoints = new List<Vector3>();
        if (controlPoints.Count < 2)
        {
            if (controlPoints.Count == 1) splinePoints.Add(controlPoints[0]);
            return splinePoints;
        }

        List<Vector3> tempPoints = new List<Vector3>(controlPoints);

        if (config.curveType == CurveType.NoCurve) // Use config.curveType
        {
            splinePoints.AddRange(tempPoints);
            if (closedLoop && tempPoints.Count >= 2)
            {
                splinePoints.Add(tempPoints[0]); // Close the loop with a straight line
            }
        }
        else if (config.curveType == CurveType.CatmullRom) // Use config.curveType
        {
            // Catmull-Rom requires padding for closed loops or short point counts
            if (closedLoop && controlPoints.Count >= 3)
            {
                tempPoints.Insert(0, controlPoints[controlPoints.Count - 1]); // Add last point at start
                tempPoints.Add(controlPoints[0]); // Add first point at end
                tempPoints.Add(controlPoints[1]); // Add second point at end
            }
            else // Handle open loops or insufficient points for full wrap
            {
                if (controlPoints.Count == 2)
                {
                    tempPoints.Insert(0, controlPoints[0]); // Duplicate first for start
                    tempPoints.Add(controlPoints[1]); // Duplicate last for end
                }
                else if (controlPoints.Count > 0)
                {
                    // This condition is already mostly handled by the above but ensures at least 4 points for the loop below
                    // If there are less than 4 points, Catmull-Rom needs special handling or won't draw
                    while (tempPoints.Count < 4)
                    {
                        tempPoints.Insert(0, tempPoints[0]);
                        tempPoints.Add(tempPoints[tempPoints.Count - 1]);
                    }
                }
            }

            for (int i = 0; i < tempPoints.Count - 3; i++)
            {
                for (int j = 0; j <= config.curveSegments; j++) // Use config.curveSegments
                {
                    float t = (float)j / config.curveSegments;
                    if (j == 0 && i > 0) continue; // Avoid duplicating points between segments

                    Vector3 interpolatedPoint = CalculateCatmullRom(tempPoints[i], tempPoints[i + 1], tempPoints[i + 2], tempPoints[i + 3], t);
                    splinePoints.Add(ConformPointToGround(interpolatedPoint, config)); // Pass config
                }
            }
        }
        else if (config.curveType == CurveType.FilletCorners)
        {
            if (controlPoints.Count < 2)
            {
                if (controlPoints.Count == 1) splinePoints.Add(ConformPointToGround(controlPoints[0], config));
                return splinePoints;
            }

            int numControlPoints = controlPoints.Count;
            for (int i = 0; i < numControlPoints; i++)
            {
                Vector3 pPrev = controlPoints[(i - 1 + numControlPoints) % numControlPoints];
                Vector3 pCurrent = controlPoints[i];
                Vector3 pNext = controlPoints[(i + 1) % numControlPoints];

                // Calculate the vectors for the incoming and outgoing segments
                Vector3 incomingVec = (pCurrent - pPrev).normalized;
                Vector3 outgoingVec = (pNext - pCurrent).normalized;

                // Determine the length to offset for the fillet.
                // We'll use a portion of the shorter segment's length to prevent overshooting.
                float incomingLen = Vector3.Distance(pPrev, pCurrent);
                float outgoingLen = Vector3.Distance(pCurrent, pNext);
                float segmentMinLength = Mathf.Min(incomingLen, outgoingLen);

                // Ensure fillet points don't go past the previous/next control points
                float actualFilletAmount = Mathf.Min(config.filletAmount, (segmentMinLength * 0.5f)); // Scale fillet by segment length
                actualFilletAmount = Mathf.Clamp01(actualFilletAmount); // Ensure it's between 0 and 1

                // Calculate the start and end points of the fillet curve
                Vector3 filletStart = pCurrent - incomingVec * actualFilletAmount;
                Vector3 filletEnd = pCurrent + outgoingVec * actualFilletAmount;

                // Conform these temporary points to the ground
                filletStart = ConformPointToGround(filletStart, config); // Pass config
                filletEnd = ConformPointToGround(filletEnd, config); // Pass config

                // Add straight line segment leading to the fillet
                // If it's the first point of a closed loop, the previous straight segment comes from the end of the last fillet.
                if (config.filletAmount > 0.001f) // Only add straight parts if there's an actual fillet
                {
                    // Add points for the straight line segment between the end of the *previous* fillet curve and the start of the *current* fillet curve.
                    // Or for the very first segment (open loop), from pPrev to filletStart.
                    if (i == 0 && !closedLoop)
                    {
                        splinePoints.Add(ConformPointToGround(pPrev, config));
                    }
                    if (splinePoints.Any() && Vector3.Distance(splinePoints.Last(), filletStart) > 0.001f)
                    {
                        int straightSegments = Mathf.Max(1, config.curveSegments); // Use config.curveSegments
                        for (int j = 1; j <= straightSegments; j++) // Start from 1 as 0 is previous point
                        {
                            float t = (float)j / straightSegments;
                            splinePoints.Add(ConformPointToGround(Vector3.Lerp(splinePoints.Last(), filletStart, t), config));
                        }
                    }
                }
                else // If filletAmount is 0, just add straight lines between original points
                {
                    splinePoints.Add(ConformPointToGround(pCurrent, config));
                    continue; // Skip curve generation if no fillet
                }


                // Add points for the fillet curve itself
                for (int j = 0; j <= config.curveSegments; j++) // Use config.curveSegments
                {
                    float t = (float)j / config.curveSegments;
                    if (j == 0 && splinePoints.Any() && Vector3.Distance(splinePoints.Last(), filletStart) < 0.001f) continue;

                    Vector3 interpolatedPoint = CalculateQuadraticBezier(filletStart, pCurrent, filletEnd, t);
                    splinePoints.Add(ConformPointToGround(interpolatedPoint, config)); // Pass config
                }

                // For the last segment of an open loop, ensure the very last control point is added as well.
                if (!closedLoop && i == numControlPoints - 1 && Vector3.Distance(splinePoints.Last(), ConformPointToGround(pNext, config)) > 0.001f)
                {
                     // Add the final straight segment after the last fillet.
                    int straightSegments = Mathf.Max(1, config.curveSegments); // Use config.curveSegments
                    for (int j = 1; j <= straightSegments; j++)
                    {
                        float t = (float)j / straightSegments;
                        splinePoints.Add(ConformPointToGround(Vector3.Lerp(splinePoints.Last(), pNext, t), config));
                    }
                }
            }
             // For closed loops with fillets, the last fillet's end needs to connect to the first fillet's start
            if (closedLoop && splinePoints.Any() && Vector3.Distance(splinePoints.Last(), splinePoints.First()) > 0.001f)
            {
                int straightSegments = Mathf.Max(1, config.curveSegments); // Use config.curveSegments
                for (int j = 1; j <= straightSegments; j++)
                {
                    float t = (float)j / straightSegments;
                    splinePoints.Add(ConformPointToGround(Vector3.Lerp(splinePoints.Last(), splinePoints.First(), t), config));
                }
            }

            // If fillet amount is 0, it should behave like NoCurve (straight lines)
            if (config.filletAmount < 0.001f && controlPoints.Count > 1) // Use config.filletAmount
            {
                splinePoints.Clear();
                splinePoints.AddRange(controlPoints);
                if (closedLoop) splinePoints.Add(controlPoints[0]);
            }
        }

        return splinePoints;
    }

    /// <summary>
    /// Calculates a point on a Catmull-Rom spline.
    /// </summary>
    private Vector3 CalculateCatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
    {
        float t2 = t * t;
        float t3 = t2 * t;

        // Catmull-Rom equations for X, Y, Z components
        float a = 0.5f * (2f * p1.x + (-p0.x + p2.x) * t + (2f * p0.x - 5f * p1.x + 4f * p2.x - p3.x) * t2 + (-p0.x + 3f * p1.x - 3f * p2.x + p3.x) * t3);
        float b = 0.5f * (2f * p1.y + (-p0.y + p2.y) * t + (2f * p0.y - 5f * p1.y + 4f * p2.y - p3.y) * t2 + (-p0.y + 3f * p1.y - 3f * p2.y + p3.y) * t3);
        float c = 0.5f * (2f * p1.z + (-p0.z + p2.z) * t + (2f * p0.z - 5f * p1.z + 4f * p2.z - p3.z) * t2 + (-p0.z + 3f * p1.z - 3f * p2.z + p3.z) * t3);

        return new Vector3(a, b, c);
    }

    /// <summary>
    /// Calculates a point on a Quadratic Bézier spline.
    /// </summary>
    private Vector3 CalculateQuadraticBezier(Vector3 p0, Vector3 p1, Vector3 p2, float t)
    {
        float u = 1 - t;
        float uu = u * u;
        float tt = t * t;

        Vector3 p = uu * p0; // (1-t)^2 * P0
        p += 2 * u * t * p1; // 2 * (1-t) * t * P1
        p += tt * p2; // t^2 * P2

        return p;
    }


    /// <summary>
    /// Helper method to conform a point to the ground layer with offset.
    /// </summary>
    private Vector3 ConformPointToGround(Vector3 point, MeshConfiguration config) // Added config parameter
    {
        // Raycast down to conform to groundLayer
        RaycastHit hit;
        Vector3 rayOrigin = new Vector3(point.x, Camera.main.transform.position.y + 100f, point.z);
        float raycastDistance = Camera.main.transform.position.y + 200f; // Sufficiently long ray

        if (Physics.Raycast(rayOrigin, Vector3.down, out hit, raycastDistance, config.groundLayer)) // Use config.groundLayer
        {
            point.y = hit.point.y + config.groundOffset; // Use config.groundOffset
        }
        else
        {
            Debug.LogWarning($"Spline point at X:{point.x}, Z:{point.z} did not hit ground. Point will use its calculated Y.");
        }
        return point;
    }


    /// <summary>
    /// Generates the 3D mesh based on the control points and settings.
    /// </summary>
    void GenerateMesh()
    {
        MeshConfiguration config = GetActiveConfig();
        if (config == null)
        {
            Debug.LogError("No active MeshConfiguration to generate mesh from. This should not happen if a drawing session is active.");
            return;
        }

        if (controlPoints.Count < 3)
        {
            Debug.LogWarning("Need at least 3 points to form a polygon. Current control points: " + controlPoints.Count);
            return;
        }

        Mesh newGeneratedMesh = new Mesh();
        List<Vector3> vertices = new List<Vector3>();
        List<int> triangles = new List<int>();
        List<Vector2> uvs = new List<Vector2>();

        // Generate points for the outer spline (main perimeter of the mesh)
        List<Vector3> splineMeshPoints = GenerateSplinePoints(true, config);

        if (splineMeshPoints.Count < 3)
        {
            Debug.LogWarning("Not enough spline points generated to form a mesh.");
            return;
        }

        // Calculate centroid of the outer spline points
        Vector3 centroid = Vector3.zero;
        foreach (Vector3 p in splineMeshPoints)
        {
            centroid += p;
        }
        centroid /= splineMeshPoints.Count;

        // Raycast centroid to ground for accurate Y position
        RaycastHit centroidHit;
        Vector3 rayOriginCentroid = new Vector3(centroid.x, Camera.main.transform.position.y + 100f, centroid.z);
        if (Physics.Raycast(rayOriginCentroid, Vector3.down, out centroidHit, Camera.main.transform.position.y + 200f, config.groundLayer)) // Use config.groundLayer
        {
            centroid.y = centroidHit.point.y + config.groundOffset; // Use config.groundOffset
        }
        else
        {
            Debug.LogWarning("Centroid did not hit ground. Using calculated Y.");
        }

        // Generate points for the first inner spline
        List<Vector3> innerSplinePoints = new List<Vector3>();
        foreach (Vector3 p in splineMeshPoints)
        {
            Vector3 innerPoint = Vector3.Lerp(centroid, p, config.innerLoopScale); // Use config.innerLoopScale
            RaycastHit innerHit;
            Vector3 rayOriginInner = new Vector3(innerPoint.x, Camera.main.transform.position.y + 100f, innerPoint.z);
            if (Physics.Raycast(rayOriginInner, Vector3.down, out innerHit, Camera.main.transform.position.y + 200f, config.groundLayer)) // Use config.groundLayer
            {
                innerPoint.y = innerHit.point.y + config.groundOffset; // Use config.groundOffset
            }
            else
            {
                Debug.LogWarning($"Inner spline point at X:{innerPoint.x}, Z:{innerPoint.z} did not hit ground. Point will use its calculated Y.");
            }
            innerSplinePoints.Add(innerPoint);
        }

        // Generate points for the innermost spline
        List<Vector3> innermostSplinePoints = new List<Vector3>();
        foreach (Vector3 p in splineMeshPoints)
        {
            Vector3 innermostPoint = Vector3.Lerp(centroid, p, config.innermostLoopScale); // Use config.innermostLoopScale
            RaycastHit innermostHit;
            Vector3 rayOriginInnermost = new Vector3(innermostPoint.x, Camera.main.transform.position.y + 100f, innermostPoint.z);
            if (Physics.Raycast(rayOriginInnermost, Vector3.down, out innermostHit, Camera.main.transform.position.y + 200f, config.groundLayer)) // Use config.groundLayer
            {
                innermostPoint.y = innermostHit.point.y + config.groundOffset; // Use config.groundOffset
            }
            else
            {
                Debug.LogWarning($"Innermost spline point at X:{innermostPoint.x}, Z:{innermostPoint.z} did not hit ground. Point will use its calculated Y.");
            }
            innermostSplinePoints.Add(innermostPoint);
        }

        // Create the new GameObject for the mesh
        finalizedMeshGameObject = new GameObject("GeneratedMesh_" + System.DateTime.Now.ToString("HHmmss"));
        MeshFilter newMeshFilter = finalizedMeshGameObject.AddComponent<MeshFilter>();
        MeshRenderer newMeshRenderer = finalizedMeshGameObject.AddComponent<MeshRenderer>();
        MeshCollider newMeshCollider = finalizedMeshGameObject.AddComponent<MeshCollider>();
        newMeshCollider.convex = config.makeConvex; // Use config.makeConvex

        // Set the mesh GameObject's position to the centroid for easier manipulation
        finalizedMeshGameObject.transform.position = centroid;

        // Set the layer of the generated mesh
        int layerIndex = LayerMask.NameToLayer(config.generatedMeshLayerName); // Use config.generatedMeshLayerName
        if (layerIndex == -1)
        {
            Debug.LogWarning($"Layer '{config.generatedMeshLayerName}' not found. Defaulting to layer 'Default' (0). Please check your Layer settings in Unity.");
            layerIndex = 0;
        }
        finalizedMeshGameObject.layer = layerIndex;

        // Convert world-space spline points to local-space relative to the new GameObject's centroid
        Vector3[] localSplineVertices = new Vector3[splineMeshPoints.Count];
        Vector3[] localInnerSplineVertices = new Vector3[innerSplinePoints.Count];
        Vector3[] localInnermostSplineVertices = new Vector3[innermostSplinePoints.Count];

        for (int i = 0; i < splineMeshPoints.Count; i++)
        {
            localSplineVertices[i] = splineMeshPoints[i] - centroid;
            localInnerSplineVertices[i] = innerSplinePoints[i] - centroid;
            localInnermostSplineVertices[i] = innermostSplinePoints[i] - centroid;
        }

        // Add all local vertices to the main vertices list
        vertices.AddRange(localSplineVertices);
        vertices.AddRange(localInnerSplineVertices);
        vertices.AddRange(localInnermostSplineVertices);

        // Calculate bounds for UV mapping (based on the original outer spline, then expanded)
        Vector3 minBounds = localSplineVertices[0];
        Vector3 maxBounds = localSplineVertices[0];
        foreach (Vector3 p in localSplineVertices) { minBounds = Vector3.Min(minBounds, p); maxBounds = Vector3.Max(maxBounds, p); }
        foreach (Vector3 p in localInnerSplineVertices) { minBounds = Vector3.Min(minBounds, p); maxBounds = Vector3.Max(maxBounds, p); }
        foreach (Vector3 p in localInnermostSplineVertices) { minBounds = Vector3.Min(minBounds, p); maxBounds = Vector3.Max(maxBounds, p); }

        float rangeX = maxBounds.x - minBounds.x;
        float rangeZ = maxBounds.z - minBounds.z;
        if (rangeX == 0) rangeX = 1f; // Avoid division by zero
        if (rangeZ == 0) rangeZ = 1f; // Avoid division by zero

        // Generate UVs for each loop
        for (int i = 0; i < localSplineVertices.Length; i++)
        {
            uvs.Add(new Vector2((localSplineVertices[i].x - minBounds.x) / rangeX, (localSplineVertices[i].z - minBounds.z) / rangeZ)); // Corrected UV.y to use rangeZ
        }
        for (int i = 0; i < localInnerSplineVertices.Length; i++)
        {
            uvs.Add(new Vector2((localInnerSplineVertices[i].x - minBounds.x) / rangeX, (localInnerSplineVertices[i].z - minBounds.z) / rangeZ)); // Corrected UV.y to use rangeZ
        }
        for (int i = 0; i < localInnermostSplineVertices.Length; i++)
        {
            uvs.Add(new Vector2((localInnermostSplineVertices[i].x - minBounds.x) / rangeX, (localInnermostSplineVertices[i].z - minBounds.z) / rangeZ)); // Corrected UV.y to use rangeZ
        }


        // Calculate indices for triangle creation
        int numPointsPerLoop = splineMeshPoints.Count;
        int outerLoopStartIdx = 0;
        int innerLoopStartIdx = numPointsPerLoop;
        int innermostLoopStartIdx = 2 * numPointsPerLoop; // Correctly 2x for three loops

        // Create triangles for the outer ring (outer spline to inner spline)
        for (int i = 0; i < numPointsPerLoop; i++)
        {
            int currentOuter = outerLoopStartIdx + i;
            int nextOuter = outerLoopStartIdx + (i + 1) % numPointsPerLoop;

            int currentInner = innerLoopStartIdx + i;
            int nextInner = innerLoopStartIdx + (i + 1) % numPointsPerLoop;

            AddTriangle(triangles, currentOuter, nextOuter, currentInner, config.invertMesh); // Use config.invertMesh
            AddTriangle(triangles, nextOuter, nextInner, currentInner, config.invertMesh); // Use config.invertMesh
        }

        // Create triangles for the middle ring (inner spline to innermost spline)
        for (int i = 0; i < numPointsPerLoop; i++)
        {
            int currentInner = innerLoopStartIdx + i;
            int nextInner = innerLoopStartIdx + (i + 1) % numPointsPerLoop;

            int currentInnermost = innermostLoopStartIdx + i;
            int nextInnermost = innermostLoopStartIdx + (i + 1) % numPointsPerLoop;

            AddTriangle(triangles, currentInner, nextInner, currentInnermost, config.invertMesh); // Use config.invertMesh
            AddTriangle(triangles, nextInner, nextInnermost, currentInnermost, config.invertMesh); // Use config.invertMesh
        }

        // Handle the innermost part of the mesh:
        // If innermostLoopScale is very small (collapses to centroid), connect innermost points to a central vertex.
        // Otherwise, create a fan of triangles from the first innermost point to fill the polygon.
        if (config.innermostLoopScale <= 0.001f) // Effectively a centroid // Use config.innermostLoopScale
        {
            vertices.Add(Vector3.zero); // Add the centroid vertex (which is 0,0,0 in local space)
            uvs.Add(new Vector2((centroid.x - minBounds.x) / rangeX, (centroid.z - minBounds.z) / rangeZ)); // UV for centroid
            int centroidIdx = vertices.Count - 1;

            for (int i = 0; i < numPointsPerLoop; i++)
            {
                int currentInnermost = innermostLoopStartIdx + i;
                int nextInnermost = innermostLoopStartIdx + (i + 1) % numPointsPerLoop;
                AddTriangle(triangles, currentInnermost, nextInnermost, centroidIdx, config.invertMesh); // Use config.invertMesh
            }
        }
        else // Innermost loop still forms a polygon, fill it
        {
            int firstInnermostPointIdx = innermostLoopStartIdx;
            // Create a fan of triangles from the first innermost point to fill the center polygon
            for (int i = 1; i < numPointsPerLoop - 1; i++)
            {
                AddTriangle(triangles, firstInnermostPointIdx, innermostLoopStartIdx + i, innermostLoopStartIdx + i + 1, config.invertMesh); // Use config.invertMesh
            }
        }


        newGeneratedMesh.vertices = vertices.ToArray();
        newGeneratedMesh.triangles = triangles.ToArray();
        newGeneratedMesh.uv = uvs.ToArray();

        newGeneratedMesh.RecalculateNormals();
        newGeneratedMesh.RecalculateBounds();

        newMeshFilter.mesh = newGeneratedMesh;
        newMeshCollider.sharedMesh = newGeneratedMesh;

        if (config.meshMaterial != null) // Use config.meshMaterial
        {
            newMeshRenderer.material = config.meshMaterial;
        }
        else
        {
            Debug.LogWarning("Mesh material not assigned for " + finalizedMeshGameObject.name + ". Using default Standard material.");
            newMeshRenderer.material = new Material(Shader.Find("Standard"));
        }

        // Add other specified components (Rigidbody, custom scripts, etc.)
        AddComponentsToMeshGameObject(finalizedMeshGameObject, config); // Pass config

        // --- NEW: Fill the ENTIRE mesh with objects using the OUTER spline points ---
        if (config.fillObjectPrefab != null && config.fillObjectSpacing > 0) // Use config.fillObjectPrefab and config.fillObjectSpacing
        {
            FillMeshWithObjects(finalizedMeshGameObject, splineMeshPoints, config); // Pass the outermost spline points and config
        }
        else if (config.fillObjectPrefab != null && config.fillObjectSpacing <= 0)
        {
            Debug.LogWarning("Fill Object Prefab is assigned, but Fill Object Spacing is zero or negative. No objects will be filled.");
        }
        // --- END NEW ---

        // The spawner's LineRenderer is now hidden, and its MeshRenderer is also hidden.
        // The new GameObject will display the mesh.
        // The LineMeshManager (spawner) itself does not have a MeshRenderer now, only a LineRenderer for preview
        lineRenderer.enabled = false;

        Debug.Log(finalizedMeshGameObject.name + " mesh generated with " + newGeneratedMesh.vertexCount + " vertices and " + newGeneratedMesh.triangles.Length / 3 + " triangles.");
    }

    /// <summary>
    /// Helper to add triangle indices, handling inversion for normals.
    /// </summary>
    private void AddTriangle(List<int> list, int v1, int v2, int v3, bool invert)
    {
        if (!invert)
        {
            list.Add(v1);
            list.Add(v2);
            list.Add(v3);
        }
        else
        {
            list.Add(v1);
            list.Add(v3);
            list.Add(v2);
        }
    }

    /// <summary>
    /// Copies specified components from the inspector list to the newly generated mesh GameObject.
    /// </summary>
    private void AddComponentsToMeshGameObject(GameObject targetGameObject, MeshConfiguration config) // Added config parameter
    {
        if (config.componentsToCopy == null || config.componentsToCopy.Length == 0) // Use config.componentsToCopy
        {
            Debug.Log("No additional components specified to add to " + targetGameObject.name);
            return;
        }

        foreach (Component sourceComponent in config.componentsToCopy) // Use config.componentsToCopy
        {
            if (sourceComponent == null)
            {
                Debug.LogWarning("Skipping null component in the 'Components To Copy' list. Ensure all slots are filled.");
                continue;
            }

            Type componentType = sourceComponent.GetType();

            // Ensure it's a concrete Unity Component type that can be added
            if (typeof(Component).IsAssignableFrom(componentType) && !componentType.IsAbstract && !componentType.IsInterface)
            {
                try
                {
                    Component newComponent = targetGameObject.AddComponent(componentType);
                    // Attempt to copy serializable fields using JsonUtility
                    string jsonData = JsonUtility.ToJson(sourceComponent);
                    JsonUtility.FromJsonOverwrite(jsonData, newComponent);

                    Debug.Log($"Added component '{componentType.Name}' to {targetGameObject.name} and attempted to copy its serializable data.");
                }
                catch (Exception e)
                {
                    Debug.LogError($"Failed to add or copy component '{componentType.Name}' to {targetGameObject.name}. Error: {e.Message}");
                }
            }
            else
            {
                Debug.LogWarning($"The object in 'Components To Copy' slot for '{componentType.Name}' is not a concrete Unity Component type that can be added to a GameObject. Please ensure you are dragging a specific component instance (e.g., Rigidbody, not a generic Component).");
            }
        }
    }

    /// <summary>
    /// Fills the inside of the generated mesh with a grid of specified objects.
    /// Objects are parented directly under the `parentMeshGameObject`.
    /// </summary>
    /// <param name="parentMeshGameObject">The parent GameObject to contain the filled objects.</param>
    /// <param name="polygonPoints">The list of points defining the polygon to fill (these are expected to be in world space).</param>
    private void FillMeshWithObjects(GameObject parentMeshGameObject, List<Vector3> polygonPoints, MeshConfiguration config) // Added config parameter
    {
        if (polygonPoints == null || polygonPoints.Count < 3)
        {
            Debug.LogWarning("Cannot fill mesh: Polygon points are insufficient (less than 3).");
            return;
        }

        if (config.fillObjectPrefab == null) // Use config.fillObjectPrefab
        {
            Debug.LogWarning("Fill Object Prefab is not assigned. Cannot fill mesh with objects.");
            return;
        }

        // Calculate the bounding box of the polygon points (2D, XZ plane)
        float minX = float.MaxValue, maxX = float.MinValue;
        float minZ = float.MaxValue, maxZ = float.MinValue;

        foreach (Vector3 p in polygonPoints)
        {
            if (p.x < minX) minX = p.x;
            if (p.x > maxX) maxX = p.x;
            if (p.z < minZ) minZ = p.z;
            if (p.z > maxZ) maxZ = p.z;
        }

        int count = 0;
        // Iterate through a grid covering the bounding box
        // Start slightly inside minX/minZ to ensure first point is covered by grid spacing
        for (float x = minX; x <= maxX; x += config.fillObjectSpacing) // Use config.fillObjectSpacing
        {
            for (float z = minZ; z <= maxZ; z += config.fillObjectSpacing) // Use config.fillObjectSpacing
            {
                Vector3 testPoint = new Vector3(x, 0, z); // Y doesn't matter for 2D point-in-polygon test

                // Check if the point is inside the polygon (top-down view)
                if (IsPointInPolygon(testPoint, polygonPoints))
                {
                    RaycastHit hit;
                    // Raycast down from a high point to find the ground position
                    Vector3 rayOrigin = new Vector3(x, Camera.main.transform.position.y + 100f, z);
                    float raycastDistance = Camera.main.transform.position.y + 200f;

                    if (Physics.Raycast(rayOrigin, Vector3.down, out hit, raycastDistance, config.groundLayer)) // Use config.groundLayer
                    {
                        Vector3 spawnPosition = hit.point + Vector3.up * (config.groundOffset + config.fillOffset); // Apply fillOffset here // Use config.groundOffset and config.fillOffset
                        // Set the parent directly to the generated mesh GameObject
                        GameObject obj = Instantiate(config.fillObjectPrefab, spawnPosition, Quaternion.identity, parentMeshGameObject.transform); // Use config.fillObjectPrefab
                        count++;
                    }
                    else
                    {
                        // Optional: Debug this warning if you want to know about points that didn't hit ground
                        // Debug.LogWarning($"Fill point at X:{x}, Z:{z} did not hit ground on layer {LayerMask.LayerToName(groundLayer.value)}. Skipping object instantiation.");
                    }
                }
            }
        }
        Debug.Log($"Filled mesh area with {count} objects.");
    }

    /// <summary>
    /// Determines if a 2D point (testPoint.x, testPoint.z) is inside a 2D polygon
    /// defined by a list of 3D points (polygonPoints.x, polygonPoints.z).
    /// Uses the ray-casting (odd-even) algorithm. Assumes polygonPoints are ordered.
    /// </summary>
    /// <param name="testPoint">The point to test (Y-coordinate is ignored for the check).</param>
    /// <param name="polygonPoints">A list of Vector3 points defining the polygon (their Y-coordinates are ignored).</param>
    /// <returns>True if the point is inside the polygon, false otherwise.</returns>
    private bool IsPointInPolygon(Vector3 testPoint, List<Vector3> polygonPoints)
    {
        if (polygonPoints.Count < 3) return false; // A polygon needs at least 3 vertices

        int crossings = 0;
        int numPoints = polygonPoints.Count;

        for (int i = 0; i < numPoints; i++)
        {
            Vector3 p1 = polygonPoints[i];
            Vector3 p2 = polygonPoints[(i + 1) % numPoints]; // Wrap around for the last segment

            // Check if the ray from testPoint.x+ (to positive X) crosses the segment (p1, p2)
            // Simplified winding number check:
            // 1. Check if the segment crosses the horizontal line at testPoint.z
            // 2. If it crosses, check if the crossing point's X is to the right of testPoint.x

            bool segmentCrossesHorizontalLine = ((p1.z <= testPoint.z && p2.z > testPoint.z) || (p1.z > testPoint.z && p2.z <= testPoint.z));

            if (segmentCrossesHorizontalLine)
            {
                // Calculate the X-coordinate of the intersection point
                float intersectX = p1.x + (testPoint.z - p1.z) / (p2.z - p1.z) * (p2.x - p1.x);

                // If the intersection is to the right of testPoint.x, it's a crossing
                if (intersectX > testPoint.x)
                {
                    crossings++;
                }
            }
        }
        // If the number of crossings is odd, the point is inside the polygon
        return (crossings % 2 == 1);
    }
}
