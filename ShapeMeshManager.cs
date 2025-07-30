using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems; // Required for UI checks
using UnityEngine.UI; // REQUIRED for Button type
using System; // Required for Type
using System.Collections; // Required for Coroutines

// Ensure CropManager.cs is in a folder like Assets/Scripts/CORE/farming/
// and that it matches the user-provided structure.

[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class ShapeMeshManager : MonoBehaviour
{
    [Header("Manager Settings")]
    [Tooltip("Check this box ONLY for the ShapeMeshManager GameObject you place directly in the scene. This instance will manage spawning new meshes.")]
    public bool isSpawner = false;

    // NEW: Separate arrays for activating drawing modes
    [Header("Activation Settings")]
    [Tooltip("Drag the UI Buttons here that should activate BOX drawing.")]
    public Button[] activateBoxDrawingButtons;
    [Tooltip("Drag the UI Buttons here that should activate CIRCLE drawing.")]
    public Button[] activateCircleDrawingButtons;

    [Header("Mesh Creation Settings")]
    [Tooltip("Material for the generated mesh. IMPORTANT: Assign a visible material here.")]
    public Material meshMaterial;

    [Tooltip("Material to use if the generated mesh is on an invalid slope.")]
    public Material invalidMaterial;

    [Header("Normal Settings")]
    [Tooltip("If true, the mesh normals will be inverted (making the inside surface visible from the outside).")]
    public bool invertMesh = false;

    [Header("Ground Conformance")]
    [Tooltip("The LayerMask for the ground object(s) the mesh should conform to.")]
    public LayerMask groundLayer;
    [Tooltip("Offset above the ground where the mesh vertices will be placed.")]
    public float groundOffset = 0.05f;

    [Header("Mesh Generation Details")]
    [Tooltip("If true, the MeshCollider will be marked as convex. Convex MeshColliders can interact with other convex colliders.")]
    public bool makeConvex = false;

    [Header("Circle Draw Settings")] // Settings for circle drawing
    [Tooltip("Number of segments for the circle mesh. Higher values make the circle smoother.")]
    [Range(8, 64)]
    public int circleSegments = 32;

    [Header("Slope Detection Settings")] // Slope detection settings
    [Tooltip("Maximum angle (in degrees) from vertical that the ground can have for the shape to be considered on a valid slope.")]
    [Range(0, 90)]
    public float maxSlopeAngle = 45f; // Default max slope angle

    // NEW: Invalid Placement UI Settings
    [Header("Invalid Placement UI")]
    [Tooltip("Drag UI GameObjects (e.g., text, image panels) here to be enabled when an invalid slope is detected.")]
    public GameObject[] invalidPlacementUIElements;
    [Tooltip("Duration in seconds that the invalid placement UI will remain visible.")]
    public float invalidUINoticeDuration = 2.0f; // Default to 2 seconds

    // NEW: Child Object Settings - ADDED THESE FIELDS
    [Header("Child Object Settings")]
    [Tooltip("Toggle to enable/disable spawning child objects.")]
    public bool spawnChildObjects = false;
    [Tooltip("Drag prefabs or GameObjects here to be spawned as children of the generated mesh.")]
    public GameObject[] childObjectsToSpawn;

    // Components to copy to the generated mesh
    [Header("Dynamic Components")]
    [Tooltip("Drag and drop components (e.g., Rigidbody, Collider, your custom scripts) from other GameObjects here. Their type and *serializable* data will be copied to the generated mesh. Note: Only serializable fields will be copied.")]
    public Component[] componentsToCopy;


    // Internal state for drawing
    private Vector3? startDragPoint = null;
    private MeshFilter previewMeshFilter;
    private MeshRenderer previewMeshRenderer;

    private static ShapeMeshManager currentDrawingInstance;
    private static bool isDrawingSessionActive = false;
    private DrawingType _activeDrawingType = DrawingType.None; // New internal state for active drawing type

    private enum DrawingType
    {
        None,
        Box,
        Circle
    }

    // Store the last valid end drag point for clamping size
    private Vector3 _lastValidEndDragPoint;
    private bool _isCurrentlyInvalidSlope = false;

    private Coroutine _fadeInvalidUICoroutine; // To manage the UI fade-out


    void Awake()
    {
        previewMeshFilter = GetComponent<MeshFilter>();
        previewMeshRenderer = GetComponent<MeshRenderer>();
        previewMeshRenderer.enabled = false;

        // Ensure UI elements are initially disabled
        DisableInvalidPlacementUI();

        if (meshMaterial == null)
        {
            Debug.LogError("Mesh Material is not assigned in ShapeMeshManager. Please assign one in the Inspector.");
        }
        if (invalidMaterial == null)
        {
            Debug.LogWarning("Invalid Material is not assigned in ShapeMeshManager. Shapes on invalid slopes will use the regular mesh material for preview/finalization and a warning will be logged.");
        }
    }

    void OnEnable()
    {
        if (isSpawner)
        {
            // Subscribe for Box Drawing buttons
            if (activateBoxDrawingButtons != null)
            {
                foreach (Button button in activateBoxDrawingButtons)
                {
                    if (button != null)
                    {
                        button.onClick.AddListener(StartNewBoxDrawingSession);
                        Debug.Log($"Subscribed to '{button.name}' for Box Drawing.");
                    }
                }
            }

            // Subscribe for Circle Drawing buttons
            if (activateCircleDrawingButtons != null)
            {
                foreach (Button button in activateCircleDrawingButtons)
                {
                    if (button != null)
                    {
                        button.onClick.AddListener(StartNewCircleDrawingSession);
                        Debug.Log($"Subscribed to '{button.name}' for Circle Drawing.");
                    }
                }
            }
        }
    }

    void OnDisable()
    {
        if (isSpawner)
        {
            // Unsubscribe for Box Drawing buttons
            if (activateBoxDrawingButtons != null)
            {
                foreach (Button button in activateBoxDrawingButtons)
                {
                    if (button != null)
                    {
                        button.onClick.RemoveListener(StartNewBoxDrawingSession);
                        Debug.Log($"Unsubscribed from '{button.name}' for Box Drawing.");
                    }
                }
            }

            // Unsubscribe for Circle Drawing buttons
            if (activateCircleDrawingButtons != null)
            {
                foreach (Button button in activateCircleDrawingButtons)
                {
                    if (button != null)
                    {
                        button.onClick.RemoveListener(StartNewCircleDrawingSession);
                        Debug.Log($"Unsubscribed from '{button.name}' for Circle Drawing.");
                    }
                }
            }
        }
    }

    void Update()
    {
        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
        {
            return;
        }

        if (isSpawner && isDrawingSessionActive && _activeDrawingType != DrawingType.None)
        {
            Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
            RaycastHit hit;

            // Start drag
            if (Input.GetMouseButtonDown(0))
            {
                if (Physics.Raycast(ray, out hit, Mathf.Infinity, groundLayer))
                {
                    startDragPoint = hit.point + Vector3.up * groundOffset;
                    _lastValidEndDragPoint = startDragPoint.Value; // Initialize last valid point
                    _isCurrentlyInvalidSlope = false; // Reset slope status
                    currentDrawingInstance = this;
                    SetupPreviewMesh();
                    Debug.Log($"{_activeDrawingType} drawing started.");
                }
                else
                {
                    Debug.LogWarning("Mouse click did not hit any object on the specified Ground Layer!");
                }
            }
            // During drag
            else if (Input.GetMouseButton(0) && startDragPoint.HasValue)
            {
                if (Physics.Raycast(ray, out hit, Mathf.Infinity, groundLayer))
                {
                    Vector3 currentPoint = hit.point + Vector3.up * groundOffset;
                    UpdatePreviewMesh(startDragPoint.Value, currentPoint);
                }
            }
            // End drag
            else if (Input.GetMouseButtonUp(0) && startDragPoint.HasValue)
            {
                if (Physics.Raycast(ray, out hit, Mathf.Infinity, groundLayer))
                {
                    // For finalization, use the _lastValidEndDragPoint if it's currently invalid,
                    // otherwise use the current endDragPoint.
                    Vector3 endDragPointForFinalization = _isCurrentlyInvalidSlope ? _lastValidEndDragPoint : (hit.point + Vector3.up * groundOffset);

                    FinalizeShapeMesh(startDragPoint.Value, endDragPointForFinalization);
                    CleanupDrawingSession(); // New method to clean up all session state
                    Debug.Log($"{_activeDrawingType} drawing session ended.");
                }
                else
                {
                    Debug.LogWarning("Mouse released but did not hit any object on the specified Ground Layer!");
                    CleanupDrawingSession();
                }
            }

            // Reset drawing with 'R' key
            if (Input.GetKeyDown(KeyCode.R))
            {
                CleanupDrawingSession();
                Debug.Log("Drawing reset by R key.");
            }
        }
    }

    // NEW: Separate methods for starting box/circle drawing
    public void StartNewBoxDrawingSession()
    {
        StartNewDrawingSession(DrawingType.Box);
    }

    public void StartNewCircleDrawingSession()
    {
        StartNewDrawingSession(DrawingType.Circle);
    }

    private void StartNewDrawingSession(DrawingType type)
    {
        if (isSpawner)
        {
            if (!isDrawingSessionActive)
            {
                isDrawingSessionActive = true;
                _activeDrawingType = type;
                Debug.Log($"Drawing session activated for {_activeDrawingType}. Click and drag on the ground to draw.");
            }
            else
            {
                Debug.LogWarning($"Cannot start a new drawing session for {type}. An existing drawing session for {_activeDrawingType} is in progress. Please finalize or reset the current drawing.");
            }
        }
        else
        {
            Debug.LogError("This ShapeMeshManager instance is not marked as the spawner. The button should call the spawner's method.");
        }
    }

    private void CleanupDrawingSession()
    {
        CleanupPreviewMesh();
        startDragPoint = null;
        isDrawingSessionActive = false;
        _activeDrawingType = DrawingType.None; // Reset active drawing type
        _isCurrentlyInvalidSlope = false; // Reset slope status
        DisableInvalidPlacementUI(); // Ensure UI is hidden when session ends
        if (_fadeInvalidUICoroutine != null)
        {
            StopCoroutine(_fadeInvalidUICoroutine);
            _fadeInvalidUICoroutine = null;
        }
    }

    private void SetupPreviewMesh()
    {
        previewMeshFilter.mesh = new Mesh();
        previewMeshRenderer.enabled = true;
        previewMeshRenderer.material = meshMaterial; // Start with valid material
    }

    private void UpdatePreviewMesh(Vector3 p1, Vector3 currentDragPoint)
    {
        List<Vector3> pointsForSlopeCheck = new List<Vector3>();
        Vector3 pointToDrawWith = currentDragPoint; // This will be the actual point used for mesh generation

        // Determine potential points for slope check based on active drawing mode
        if (_activeDrawingType == DrawingType.Box)
        {
            // For box, check the 4 corners
            Vector3 c1 = new Vector3(Mathf.Min(p1.x, currentDragPoint.x), currentDragPoint.y, Mathf.Min(p1.z, currentDragPoint.z));
            Vector3 c2 = new Vector3(Mathf.Max(p1.x, currentDragPoint.x), currentDragPoint.y, Mathf.Min(p1.z, currentDragPoint.z));
            Vector3 c3 = new Vector3(Mathf.Min(p1.x, currentDragPoint.x), currentDragPoint.y, Mathf.Max(p1.z, currentDragPoint.z));
            Vector3 c4 = new Vector3(Mathf.Max(p1.x, currentDragPoint.x), currentDragPoint.y, Mathf.Max(p1.z, currentDragPoint.z));
            pointsForSlopeCheck.AddRange(new Vector3[] { c1, c2, c3, c4 });
        }
        else if (_activeDrawingType == DrawingType.Circle)
        {
            // For circle, check the center and 8 points on circumference
            Vector3 flatCenter = new Vector3(p1.x, p1.y, p1.z);
            Vector3 flatMouse = new Vector3(currentDragPoint.x, p1.y, currentDragPoint.z);
            float radius = Vector3.Distance(flatCenter, flatMouse);

            // Add the center point for slope check, ensuring its Y is correctly raycasted
            RaycastHit centerHit;
            Vector3 centerRayOrigin = new Vector3(p1.x, Camera.main.transform.position.y + 10f, p1.z);
            if (Physics.Raycast(centerRayOrigin, Vector3.down, out centerHit, Mathf.Infinity, groundLayer))
            {
                pointsForSlopeCheck.Add(centerHit.point + Vector3.up * groundOffset);
            }
            else
            {
                pointsForSlopeCheck.Add(new Vector3(p1.x, p1.y, p1.z)); // Fallback
            }

            int numCheckPoints = Mathf.Min(8, circleSegments);
            for (int i = 0; i < numCheckPoints; i++)
            {
                float angle = (float)i / numCheckPoints * 2 * Mathf.PI;
                float x = p1.x + radius * Mathf.Cos(angle);
                float z = p1.z + radius * Mathf.Sin(angle);

                // Raycast down from above to get the actual ground Y for slope check
                Vector3 checkPointRayOrigin = new Vector3(x, Camera.main.transform.position.y + 10f, z);
                RaycastHit hit;
                if (Physics.Raycast(checkPointRayOrigin, Vector3.down, out hit, Mathf.Infinity, groundLayer))
                {
                    pointsForSlopeCheck.Add(hit.point + Vector3.up * groundOffset);
                }
                else
                {
                    // Fallback if raycast fails, point will be at p1's y-level for slope check
                    pointsForSlopeCheck.Add(new Vector3(x, p1.y, z));
                }
            }
        }

        // Check slope validity
        bool isAnySlopeInvalid = false;
        foreach (Vector3 point in pointsForSlopeCheck)
        {
            if (!CheckSlopeValidity(point))
            {
                isAnySlopeInvalid = true;
                break;
            }
        }

        // Apply visual feedback and clamp size if needed
        if (isAnySlopeInvalid)
        {
            _isCurrentlyInvalidSlope = true;
            if (invalidMaterial != null)
            {
                previewMeshRenderer.material = invalidMaterial;
            }
            else
            {
                previewMeshRenderer.material = meshMaterial; // Fallback to normal material if invalid is not set
            }
            // Clamp currentDragPoint to the last valid point
            pointToDrawWith = _lastValidEndDragPoint;

            // Show UI and start fade-out coroutine
            EnableInvalidPlacementUI();
            if (_fadeInvalidUICoroutine != null)
            {
                StopCoroutine(_fadeInvalidUICoroutine);
            }
            _fadeInvalidUICoroutine = StartCoroutine(FadeOutInvalidPlacementUI(invalidUINoticeDuration));
        }
        else
        {
            _isCurrentlyInvalidSlope = false;
            previewMeshRenderer.material = meshMaterial;
            _lastValidEndDragPoint = currentDragPoint; // Update last valid point
            pointToDrawWith = currentDragPoint; // Use the actual current drag point

            // Hide UI immediately if moving to a valid spot
            DisableInvalidPlacementUI();
            if (_fadeInvalidUICoroutine != null)
            {
                StopCoroutine(_fadeInvalidUICoroutine);
                _fadeInvalidUICoroutine = null;
            }
        }

        // Generate the preview mesh using the potentially clamped pointToDrawWith
        if (_activeDrawingType == DrawingType.Box)
        {
            GenerateQuadMesh(previewMeshFilter.mesh, p1, pointToDrawWith, transform, true);
        }
        else if (_activeDrawingType == DrawingType.Circle)
        {
            GenerateCircleMesh(previewMeshFilter.mesh, p1, pointToDrawWith, transform, true);
        }
    }

    private void CleanupPreviewMesh()
    {
        if (previewMeshFilter != null && previewMeshFilter.mesh != null)
        {
            Destroy(previewMeshFilter.mesh);
            previewMeshFilter.mesh = null;
        }
        if (previewMeshRenderer != null)
        {
            previewMeshRenderer.enabled = false;
        }
    }

    private void FinalizeShapeMesh(Vector3 p1, Vector3 p2)
    {
        // One final check for slope validity at the moment of release.
        List<Vector3> pointsForFinalSlopeCheck = new List<Vector3>();
        if (_activeDrawingType == DrawingType.Box)
        {
            Vector3 c1 = new Vector3(Mathf.Min(p1.x, p2.x), p2.y, Mathf.Min(p1.z, p2.z));
            Vector3 c2 = new Vector3(Mathf.Max(p1.x, p2.x), p2.y, Mathf.Min(p1.z, p2.z));
            Vector3 c3 = new Vector3(Mathf.Min(p1.x, p2.x), p2.y, Mathf.Max(p1.z, p2.z));
            Vector3 c4 = new Vector3(Mathf.Max(p1.x, p2.x), p2.y, Mathf.Max(p1.z, p2.z));
            pointsForFinalSlopeCheck.AddRange(new Vector3[] { c1, c2, c3, c4 });
        }
        else if (_activeDrawingType == DrawingType.Circle)
        {
            // Add the center point for final slope check, ensuring its Y is correctly raycasted
            RaycastHit centerHit;
            Vector3 centerRayOrigin = new Vector3(p1.x, Camera.main.transform.position.y + 10f, p1.z);
            if (Physics.Raycast(centerRayOrigin, Vector3.down, out centerHit, Mathf.Infinity, groundLayer))
            {
                pointsForFinalSlopeCheck.Add(centerHit.point + Vector3.up * groundOffset);
            }
            else
            {
                pointsForFinalSlopeCheck.Add(new Vector3(p1.x, p1.y, p1.z)); // Fallback
            }

            Vector3 flatCenter = new Vector3(p1.x, p2.y, p1.z);
            Vector3 flatMouse = new Vector3(p2.x, p2.y, p2.z);
            float radius = Vector3.Distance(flatCenter, flatMouse);

            int numCheckPoints = Mathf.Min(8, circleSegments);
            for (int i = 0; i < numCheckPoints; i++)
            {
                float angle = (float)i / numCheckPoints * 2 * Mathf.PI;
                float x = p1.x + radius * Mathf.Cos(angle);
                float z = p1.z + radius * Mathf.Sin(angle);

                Vector3 checkPointRayOrigin = new Vector3(x, Camera.main.transform.position.y + 10f, z);
                RaycastHit hit;
                if (Physics.Raycast(checkPointRayOrigin, Vector3.down, out hit, Mathf.Infinity, groundLayer))
                {
                    pointsForFinalSlopeCheck.Add(hit.point + Vector3.up * groundOffset);
                }
                else
                {
                    pointsForFinalSlopeCheck.Add(new Vector3(x, p1.y, z));
                }
            }
        }

        bool finalSlopeCheckInvalid = false;
        foreach (Vector3 point in pointsForFinalSlopeCheck)
        {
            if (!CheckSlopeValidity(point))
            {
                finalSlopeCheckInvalid = true;
                break;
            }
        }

        // If the final check on release shows invalid slope, abort creation.
        if (finalSlopeCheckInvalid)
        {
            Debug.LogWarning($"Placement on invalid slope detected at release for {_activeDrawingType}. Mesh creation aborted.");
            return;
        }

        // Proceed with creating the mesh only if the slope is valid at the point of release
        string uniqueMeshID = "GeneratedMesh_" + _activeDrawingType.ToString() + "_" + System.DateTime.Now.ToString("yyyyMMdd_HHmmss_fff"); // More robust unique ID
        GameObject finalizedMeshGameObject = new GameObject(uniqueMeshID);
        MeshFilter newMeshFilter = finalizedMeshGameObject.AddComponent<MeshFilter>();
        MeshRenderer newMeshRenderer = finalizedMeshGameObject.AddComponent<MeshRenderer>();
        MeshCollider newMeshCollider = finalizedMeshGameObject.AddComponent<MeshCollider>();
        newMeshCollider.convex = makeConvex;

        // --- PIVOT CENTERING LOGIC ---
        Vector3 shapeCenterWorld;
        if (_activeDrawingType == DrawingType.Box)
        {
            shapeCenterWorld = (p1 + p2) / 2f;
            shapeCenterWorld.y = (p1.y + p2.y) / 2f;
        }
        else // Circle
        {
            shapeCenterWorld = p1;
        }

        // Set the GameObject's position to the calculated center of the shape
        finalizedMeshGameObject.transform.position = shapeCenterWorld;

        if (_activeDrawingType == DrawingType.Box)
        {
            GenerateQuadMesh(newMeshFilter.mesh, p1, p2, finalizedMeshGameObject.transform, false);
        }
        else if (_activeDrawingType == DrawingType.Circle)
        {
            GenerateCircleMesh(newMeshFilter.mesh, p1, p2, finalizedMeshGameObject.transform, false);
        }

        // Apply the valid material for the finalized mesh
        if (meshMaterial != null)
        {
            newMeshRenderer.material = meshMaterial;
        }
        else
        {
            Debug.LogWarning("Mesh material not assigned for " + finalizedMeshGameObject.name + ". Using default Standard material.");
            newMeshRenderer.material = new Material(Shader.Find("Standard"));
        }

        newMeshCollider.sharedMesh = newMeshFilter.mesh;
        AddComponentsToMeshGameObject(finalizedMeshGameObject);

        // Spawn Child Objects based on toggle
        if (spawnChildObjects && childObjectsToSpawn != null && childObjectsToSpawn.Length > 0)
        {
            foreach (GameObject childPrefab in childObjectsToSpawn)
            {
                if (childPrefab != null)
                {
                    // Instantiate the prefab as a child of the generated mesh GameObject
                    // Position and rotation will be relative to the parent (0,0,0 and Quaternion.identity will place it at the parent's pivot)
                    GameObject childInstance = Instantiate(childPrefab, finalizedMeshGameObject.transform);
                    childInstance.transform.localPosition = Vector3.zero; // Place at the parent's pivot by default
                    childInstance.transform.localRotation = Quaternion.identity;
                    Debug.Log($"Spawned child object '{childPrefab.name}' under '{finalizedMeshGameObject.name}'.");
                }
                else
                {
                    Debug.LogWarning("Skipping null child object in 'Child Objects To Spawn' list. Ensure all slots are filled.");
                }
            }
        }

        Debug.Log(finalizedMeshGameObject.name + " generated with " + newMeshFilter.mesh.vertexCount + " vertices and " + newMeshFilter.mesh.triangles.Length / 3 + " triangles." +
                  $" Pivot set to center at world position: {finalizedMeshGameObject.transform.position}");
    }

    /// <summary>
    /// Checks if the ground slope at a given world point is within the allowed angle.
    /// </summary>
    /// <param name="worldPoint">The world position to check.</param>
    /// <returns>True if the slope is valid, false otherwise.</returns>
    private bool CheckSlopeValidity(Vector3 worldPoint)
    {
        RaycastHit hit;
        Vector3 rayOrigin = new Vector3(worldPoint.x, Camera.main.transform.position.y + 10f, worldPoint.z);

        if (Physics.Raycast(rayOrigin, Vector3.down, out hit, Mathf.Infinity, groundLayer))
        {
            float angle = Vector3.Angle(Vector3.up, hit.normal);
            return angle <= maxSlopeAngle;
        }
        return false;
    }


    private void GenerateQuadMesh(Mesh mesh, Vector3 p1, Vector3 p2, Transform relativeTransform, bool isPreview)
    {
        Vector3 c1 = new Vector3(Mathf.Min(p1.x, p2.x), p2.y, Mathf.Min(p1.z, p2.z));
        Vector3 c2 = new Vector3(Mathf.Max(p1.x, p2.x), p2.y, Mathf.Min(p1.z, p2.z));
        Vector3 c3 = new Vector3(Mathf.Min(p1.x, p2.x), p2.y, Mathf.Max(p1.z, p2.z));
        Vector3 c4 = new Vector3(Mathf.Max(p1.x, p2.x), p2.y, Mathf.Max(p1.z, p2.z));

        Vector3[] vertices = new Vector3[4];
        vertices[0] = relativeTransform.InverseTransformPoint(c1);
        vertices[1] = relativeTransform.InverseTransformPoint(c2);
        vertices[2] = relativeTransform.InverseTransformPoint(c4);
        vertices[3] = relativeTransform.InverseTransformPoint(c3);

        int[] triangles;
        if (!invertMesh)
        {
            triangles = new int[] { 0, 1, 2, 0, 2, 3 };
        }
        else
        {
            triangles = new int[] { 0, 2, 1, 0, 3, 2 };
        }

        Vector2[] uvs = new Vector2[4]
        {
            new Vector2(0, 0),
            new Vector2(1, 0),
            new Vector2(1, 1),
            new Vector2(0, 1)
        };

        mesh.Clear();
        mesh.vertices = vertices;
        mesh.triangles = triangles;
        mesh.uv = uvs;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
    }

    private void GenerateCircleMesh(Mesh mesh, Vector3 centerPoint, Vector3 currentMousePoint, Transform relativeTransform, bool isPreview)
    {
        Vector3 flatCenter = new Vector3(centerPoint.x, currentMousePoint.y, centerPoint.z);
        Vector3 flatMouse = new Vector3(currentMousePoint.x, currentMousePoint.y, currentMousePoint.z);
        float radius = Vector3.Distance(flatCenter, flatMouse);

        List<Vector3> vertices = new List<Vector3>();
        List<int> triangles = new List<int>();
        List<Vector2> uvs = new List<Vector2>();

        // For the center vertex (index 0), raycast to get the correct Y-position
        Vector3 centerWorldPoint = centerPoint;
        RaycastHit centerHit;
        Vector3 centerRayOrigin = new Vector3(centerPoint.x, Camera.main.transform.position.y + 100f, centerPoint.z);
        float raycastDistance = Camera.main.transform.position.y + 200f;

        if (Physics.Raycast(centerRayOrigin, Vector3.down, out centerHit, raycastDistance, groundLayer))
        {
            centerWorldPoint.y = centerHit.point.y + groundOffset;
        }
        else
        {
            Debug.LogWarning($"Circle center point at X:{centerPoint.x}, Z:{centerPoint.z} did not hit ground during mesh generation. Point will use the y-coordinate of the initial center point.");
            // Fallback to initial center point's Y if raycast fails, though this might still cause stretching if not on ground.
            // A more robust fallback could be to use currentMousePoint.y or a predetermined ground level.
        }
        vertices.Add(relativeTransform.InverseTransformPoint(centerWorldPoint)); // Add the raycasted center vertex
        uvs.Add(new Vector2(0.5f, 0.5f));

        for (int i = 1; i <= circleSegments; i++)
        {
            float angle = (float)i / circleSegments * 2 * Mathf.PI;
            float x = centerPoint.x + radius * Mathf.Cos(angle);
            float z = centerPoint.z + radius * Mathf.Sin(angle);

            Vector3 circlePointWorld = new Vector3(x, centerPoint.y, z); // Initial Y can be centerPoint.y, will be overwritten by raycast

            // Raycast down from above to get the actual ground Y for circumference points
            Vector3 pointRayOrigin = new Vector3(circlePointWorld.x, Camera.main.transform.position.y + 100f, circlePointWorld.z);
            RaycastHit hit;
            if (Physics.Raycast(pointRayOrigin, Vector3.down, out hit, raycastDistance, groundLayer))
            {
                circlePointWorld.y = hit.point.y + groundOffset;
            }
            else
            {
                Debug.LogWarning($"Circle point at X:{circlePointWorld.x}, Z:{circlePointWorld.z} did not hit ground during mesh generation. Point will use the y-coordinate of the center point.");
                circlePointWorld.y = centerPoint.y; // Fallback
            }

            vertices.Add(relativeTransform.InverseTransformPoint(circlePointWorld));
            uvs.Add(new Vector2(0.5f + 0.5f * Mathf.Cos(angle), 0.5f + 0.5f * Mathf.Sin(angle)));
        }

        int centerIndex = 0;
        for (int i = 1; i <= circleSegments; i++)
        {
            int currentOuter = i;
            int nextOuter = (i == circleSegments) ? 1 : i + 1;

            if (!invertMesh)
            {
                triangles.Add(centerIndex);
                triangles.Add(currentOuter);
                triangles.Add(nextOuter);
            }
            else
            {
                triangles.Add(centerIndex);
                triangles.Add(nextOuter);
                triangles.Add(currentOuter);
            }
        }

        mesh.Clear();
        mesh.vertices = vertices.ToArray();
        mesh.triangles = triangles.ToArray();
        mesh.uv = uvs.ToArray();
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
    }

    private void AddComponentsToMeshGameObject(GameObject targetGameObject)
    {
        if (componentsToCopy == null || componentsToCopy.Length == 0)
        {
            Debug.Log("No additional components specified to add to " + targetGameObject.name);
            return;
        }

        foreach (Component sourceComponent in componentsToCopy)
        {
            if (sourceComponent == null)
            {
                Debug.LogWarning("Skipping null component in the 'Components To Copy' list. Ensure all slots are filled.");
                continue;
            }

            Type componentType = sourceComponent.GetType();

            if (typeof(Component).IsAssignableFrom(componentType) && !componentType.IsAbstract && !componentType.IsInterface)
            {
                try
                {
                    Component newComponent = targetGameObject.AddComponent(componentType);
                    // Use JsonUtility to copy serializable fields if applicable
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

    // NEW: Coroutine to fade out (disable) the invalid placement UI
    private IEnumerator FadeOutInvalidPlacementUI(float delay)
    {
        yield return new WaitForSeconds(delay);
        DisableInvalidPlacementUI();
    }

    // NEW: Helper method to enable the invalid placement UI
    private void EnableInvalidPlacementUI()
    {
        if (invalidPlacementUIElements != null)
        {
            foreach (GameObject uiElement in invalidPlacementUIElements)
            {
                if (uiElement != null)
                {
                    uiElement.SetActive(true);
                }
            }
        }
    }

    // NEW: Helper method to disable the invalid placement UI
    private void DisableInvalidPlacementUI()
    {
        if (invalidPlacementUIElements != null)
        {
            foreach (GameObject uiElement in invalidPlacementUIElements)
            {
                if (uiElement != null)
                {
                    uiElement.SetActive(false);
                }
            }
        }
    }
}
