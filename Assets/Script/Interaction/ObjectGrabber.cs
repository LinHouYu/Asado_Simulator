using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace AsadoSimulator.Interaction
{
    /// <summary>
    /// Standalone Portal-style physics object grabbing system designed for empty Prefab usage.
    /// Features:
    /// - Ultra-smooth position & rotation tracking during movement (no jitter/stutter).
    /// - Smooth interpolation towards the crosshair (local X=0, Y=0) upon pickup.
    /// - Table collider anti-embedding & jitter prevention (lift-off + obstacle sweep clamping).
    /// - Leveling pitch/roll (X=0, Z=0 tilt flattened) + R-key Y-axis rotation.
    /// - Drop placement preview ray with landing marker on tables/floors.
    /// - Tag-filtered interaction for kitchen simulation.
    /// </summary>
    [DisallowMultipleComponent]
    public class ObjectGrabber : MonoBehaviour
    {
        [Header("Camera & References")]
        [Tooltip("Player camera. If null, automatically locates Camera.main.")]
        [SerializeField] private Camera playerCamera;

        [Tooltip("Player collider or character controller. If null, automatically located.")]
        [SerializeField] private Collider playerCollider;

        [Header("Tag & Target Filtering")]
        [Tooltip("Target tag required for an object to be grabbable.")]
        [SerializeField] private string targetTag = "Grabbable";

        [Tooltip("If true, only objects with matching tag can be grabbed.")]
        [SerializeField] private bool requireTagMatch = true;

        [Tooltip("Maximum reach distance to grab an object.")]
        [SerializeField] private float grabRange = 2.8f;

        [Tooltip("Layer mask for raycast detection.")]
        [SerializeField] private LayerMask grabLayerMask = ~0;

        [Header("Hold & Smooth Settings")]
        [Tooltip("Default distance held object floats in front of camera.")]
        [SerializeField] private float defaultHoldDistance = 1.6f;

        [Tooltip("Minimum allowable hold distance to prevent clipping into player.")]
        [SerializeField] private float minHoldDistance = 0.8f;

        [Tooltip("Duration in seconds for the smooth glide from pickup point to crosshair center.")]
        [SerializeField] private float pickupSmoothDuration = 0.22f;

        [Tooltip("Position smoothing time when player moves/turns while holding object (eliminates stutter).")]
        [Range(0.01f, 0.15f)]
        [SerializeField] private float followSmoothTime = 0.035f;

        [Tooltip("Rotation smoothing time when turning camera or rotating with R key.")]
        [Range(0.01f, 0.15f)]
        [SerializeField] private float rotateSmoothTime = 0.04f;

        [Tooltip("Physics spring force pulling the held object towards target.")]
        [SerializeField] private float holdSpringForce = 22f;

        [Tooltip("Maximum linear speed of held object.")]
        [SerializeField] private float maxHoldSpeed = 12f;

        [Tooltip("Distance threshold at which the hold automatically breaks.")]
        [SerializeField] private float breakDistance = 2.4f;

        [Header("Table Anti-Embedding & Jitter Prevention")]
        [Tooltip("Vertical lift applied on initial grab to cleanly clear table surfaces.")]
        [SerializeField] private float pickupLiftOffset = 0.08f;

        [Tooltip("Minimum clearance above surfaces beneath the held object.")]
        [SerializeField] private float surfaceClearancePadding = 0.05f;

        [Tooltip("Layers treated as solid obstacles (tables, counters, walls).")]
        [SerializeField] private LayerMask obstacleLayerMask = ~0;

        [Header("Rotation Settings (R Key)")]
        [Tooltip("Speed in degrees per second for rotating the held object around Y axis.")]
        [SerializeField] private float rKeyRotateSpeed = 135f;

        [Header("Placement Preview Ray")]
        [Tooltip("Whether to display the drop landing preview ray and marker.")]
        [SerializeField] private bool showPlacementPreview = true;

        [Tooltip("Maximum downward raycast distance for landing preview.")]
        [SerializeField] private float maxPreviewDropDistance = 4f;

        [Tooltip("Color of the drop preview ray.")]
        [SerializeField] private Color previewRayColor = new Color(0.2f, 0.85f, 1f, 0.7f);

        [Tooltip("Color of the landing target circle.")]
        [SerializeField] private Color landingMarkerColor = new Color(0.2f, 0.95f, 0.9f, 0.85f);

        // State Tracking
        private Rigidbody _heldRigidbody;
        private GrabbableObject _heldGrabbable;
        private Collider[] _heldColliders;
        private float _currentHoldDistance;
        private float _heldObjectRadius = 0.2f;

        // Smooth Movement & Tracking State
        private Vector3 _smoothedHoldTargetPos;
        private Vector3 _posSmoothVelocity;
        private Vector3 _velSmoothVelocity;
        private float _currentSmoothedDistance;
        private float _currentYaw;
        private float _yawSmoothVelocity;

        // Smooth Pickup State
        private bool _isPickupSmoothing;
        private float _pickupTimer;
        private Vector3 _pickupStartWorldPos;

        // Orientation State
        private float _heldYAngleOffset;

        // Fallback Physics State (when prop lacks GrabbableObject script)
        private bool _fallbackCachedGravity;
        private float _fallbackCachedLinearDamping;
        private float _fallbackCachedAngularDamping;
        private CollisionDetectionMode _fallbackCachedCollisionMode;
        private RigidbodyInterpolation _fallbackCachedInterpolation;

        // Placement Preview Components
        private LineRenderer _previewLineRenderer;
        private GameObject _landingMarkerObj;
        private MeshRenderer _landingMarkerRenderer;

        // Raycast Hover State (for crosshair)
        public bool IsAimingAtGrabbable { get; private set; }
        public bool IsHoldingObject => _heldRigidbody != null;
        public Rigidbody HeldRigidbody => _heldRigidbody;

        private void Awake()
        {
            InitializeReferences();
            SetupPlacementPreviewVisuals();
        }

        private void InitializeReferences()
        {
            if (playerCamera == null)
            {
                playerCamera = Camera.main;
                if (playerCamera == null)
                {
                    playerCamera = FindAnyObjectByType<Camera>();
                }
            }

            if (playerCollider == null)
            {
                var cc = GetComponentInParent<CharacterController>();
                if (cc == null) cc = FindAnyObjectByType<CharacterController>();
                if (cc != null) playerCollider = cc;
            }
        }

        private void SetupPlacementPreviewVisuals()
        {
            // Setup LineRenderer for the drop ray
            GameObject lineObj = new GameObject("DropPreviewLine");
            lineObj.transform.SetParent(transform);
            _previewLineRenderer = lineObj.AddComponent<LineRenderer>();
            _previewLineRenderer.startWidth = 0.015f;
            _previewLineRenderer.endWidth = 0.015f;
            _previewLineRenderer.material = new Material(Shader.Find("Sprites/Default"));
            _previewLineRenderer.startColor = previewRayColor;
            _previewLineRenderer.endColor = previewRayColor;
            _previewLineRenderer.positionCount = 2;
            _previewLineRenderer.enabled = false;

            // Setup landing circle marker on tables/floors (pure visual mesh, zero collider)
            _landingMarkerObj = new GameObject("LandingMarker");
            _landingMarkerObj.transform.SetParent(transform);
            var meshFilter = _landingMarkerObj.AddComponent<MeshFilter>();
            var tempPrimitive = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            meshFilter.sharedMesh = tempPrimitive.GetComponent<MeshFilter>().sharedMesh;
            DestroyImmediate(tempPrimitive);

            _landingMarkerObj.transform.localScale = new Vector3(0.35f, 0.002f, 0.35f);
            _landingMarkerRenderer = _landingMarkerObj.AddComponent<MeshRenderer>();
            _landingMarkerRenderer.material = new Material(Shader.Find("Sprites/Default"));
            _landingMarkerRenderer.material.color = landingMarkerColor;
            _landingMarkerObj.SetActive(false);
        }

        private void Update()
        {
            EnsureCameraReference();
            UpdateRaycastHoverState();
            HandleInput();
            HandleRotationInput();
            ComputeSmoothHoldTarget();
            UpdatePlacementPreview();
        }

        private void FixedUpdate()
        {
            UpdateHeldObjectPhysics();
        }

        private void EnsureCameraReference()
        {
            if (playerCamera == null)
            {
                playerCamera = Camera.main;
            }
        }

        #region Hover & Input

        private void UpdateRaycastHoverState()
        {
            IsAimingAtGrabbable = false;

            if (playerCamera == null || IsHoldingObject) return;

            if (RaycastForGrabbable(out _))
            {
                IsAimingAtGrabbable = true;
            }
        }

        private bool RaycastForGrabbable(out RaycastHit bestHit)
        {
            bestHit = default;
            if (playerCamera == null) return false;

            Ray ray = new Ray(playerCamera.transform.position, playerCamera.transform.forward);
            RaycastHit[] hits = Physics.RaycastAll(ray, grabRange, grabLayerMask, QueryTriggerInteraction.Collide);
            if (hits == null || hits.Length == 0) return false;

            Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

            for (int i = 0; i < hits.Length; i++)
            {
                var h = hits[i];
                if (h.collider == null || h.collider == playerCollider) continue;

                if (IsTargetGrabbable(h.collider.gameObject))
                {
                    bestHit = h;
                    return true;
                }

                // If blocked by a solid obstacle (not a trigger), block line of sight
                if (!h.collider.isTrigger)
                {
                    return false;
                }
            }

            return false;
        }

        private bool IsTargetGrabbable(GameObject target)
        {
            if (target == null) return false;

            if (target.TryGetComponent<GrabbableObject>(out var grabbable))
            {
                return !requireTagMatch || grabbable.CanGrabWithTag(targetTag);
            }

            if (target.TryGetComponent<Rigidbody>(out _))
            {
                return !requireTagMatch || SafeCompareTag(target, targetTag);
            }

            return false;
        }

        private bool SafeCompareTag(GameObject obj, string tagToCompare)
        {
            if (string.IsNullOrEmpty(tagToCompare)) return true;
            try
            {
                return obj.CompareTag(tagToCompare);
            }
            catch
            {
                return obj.tag == tagToCompare;
            }
        }

        private void HandleInput()
        {
            var mouse = Mouse.current;
            if (mouse == null) return;

            // Left Click -> Grab
            if (mouse.leftButton.wasPressedThisFrame)
            {
                if (!IsHoldingObject)
                {
                    TryGrabObject();
                }
            }

            // Right Click -> Drop
            if (mouse.rightButton.wasPressedThisFrame)
            {
                if (IsHoldingObject)
                {
                    DropObject();
                }
            }
        }

        private void HandleRotationInput()
        {
            if (!IsHoldingObject) return;

            var keyboard = Keyboard.current;
            if (keyboard == null) return;

            // Rotate Y axis while holding R
            if (keyboard.rKey.isPressed)
            {
                float dir = keyboard.leftShiftKey.isPressed ? -1f : 1f;
                _heldYAngleOffset += dir * rKeyRotateSpeed * Time.deltaTime;
                _heldYAngleOffset = Mathf.Repeat(_heldYAngleOffset, 360f);
            }
        }

        #endregion

        #region Grab & Drop Mechanics

        private void TryGrabObject()
        {
            if (playerCamera == null) return;

            if (!RaycastForGrabbable(out RaycastHit hit)) return;

            GameObject hitObject = hit.collider.gameObject;
            if (!IsTargetGrabbable(hitObject)) return;

            if (hitObject.TryGetComponent<GrabbableObject>(out var grabbable))
            {
                _heldGrabbable = grabbable;
                _heldRigidbody = grabbable.Rigidbody;
                _currentHoldDistance = grabbable.CustomHoldDistance > 0f ? grabbable.CustomHoldDistance : defaultHoldDistance;
                _heldGrabbable.OnGrab(playerCollider);
            }
            else if (hitObject.TryGetComponent<Rigidbody>(out var rb))
            {
                _heldGrabbable = null;
                _heldRigidbody = rb;
                _currentHoldDistance = defaultHoldDistance;
                SetupFallbackGrabPhysics(rb);
            }
            else
            {
                return;
            }

            // 1. Calculate true world collider center directly from the object's real colliders
            Vector3 initialColliderCenter = GetRealColliderCenterWorld(_heldRigidbody);

            // Measure object bounding radius from the colliders in world space
            _heldColliders = _heldRigidbody.GetComponentsInChildren<Collider>();
            Bounds bounds = new Bounds(initialColliderCenter, Vector3.zero);
            foreach (var col in _heldColliders)
            {
                if (col != null && col.enabled && !col.isTrigger) bounds.Encapsulate(col.bounds);
            }
            _heldObjectRadius = Mathf.Clamp(bounds.extents.magnitude * 0.5f, 0.05f, 0.8f);

            // Initial Pickup Lift-Off: Anchored from the real collider center
            if (hit.normal.y > 0.3f)
            {
                initialColliderCenter += Vector3.up * pickupLiftOffset;
            }

            // 2. Smooth Pickup setup
            _isPickupSmoothing = true;
            _pickupTimer = 0f;
            _pickupStartWorldPos = initialColliderCenter;
            _smoothedHoldTargetPos = initialColliderCenter;
            _posSmoothVelocity = Vector3.zero;
            _velSmoothVelocity = Vector3.zero;
            _currentSmoothedDistance = _currentHoldDistance;

            // 3. Orientation leveling: flatten pitch (X) and roll (Z) to 0, keep horizontal alignment
            float cameraYaw = playerCamera.transform.eulerAngles.y;
            float objectInitialYaw = _heldRigidbody.rotation.eulerAngles.y;
            _heldYAngleOffset = Mathf.DeltaAngle(cameraYaw, objectInitialYaw);
            _currentYaw = cameraYaw + _heldYAngleOffset;
            _yawSmoothVelocity = 0f;
        }

        public void DropObject()
        {
            if (!IsHoldingObject) return;

            Vector3 releaseVelocity = _heldRigidbody.linearVelocity;

            if (_heldGrabbable != null)
            {
                _heldGrabbable.OnDrop(releaseVelocity);
            }
            else
            {
                RestoreFallbackGrabPhysics(releaseVelocity);
            }

            _heldRigidbody = null;
            _heldGrabbable = null;
            _heldColliders = null;
            _isPickupSmoothing = false;

            HidePlacementPreview();
        }

        /// <summary>
        /// Gets the true geometric center of all active colliders on the Rigidbody in WORLD space.
        /// Automatically accounts for all child transforms, parent/root scales, and collider center offsets.
        /// </summary>
        private Vector3 GetRealColliderCenterWorld(Rigidbody rb)
        {
            if (rb == null) return Vector3.zero;

            Collider[] colliders = rb.GetComponentsInChildren<Collider>();
            Bounds combinedBounds = new Bounds();
            bool hasBounds = false;

            for (int i = 0; i < colliders.Length; i++)
            {
                var col = colliders[i];
                if (col == null || !col.enabled || !col.gameObject.activeInHierarchy || col.isTrigger) continue;

                if (!hasBounds)
                {
                    combinedBounds = col.bounds;
                    hasBounds = true;
                }
                else
                {
                    combinedBounds.Encapsulate(col.bounds);
                }
            }

            return hasBounds ? combinedBounds.center : rb.position;
        }

        #endregion

        #region Ultra-Smooth Hold & Physics

        /// <summary>
        /// Runs in Update() at full display refresh rate (60/144/240Hz) to track camera smoothly.
        /// </summary>
        private void ComputeSmoothHoldTarget()
        {
            if (!IsHoldingObject || playerCamera == null) return;

            Vector3 camPos = playerCamera.transform.position;
            Vector3 camForward = playerCamera.transform.forward;

            // 1. Anti-Embedding Sweep: dynamically shorten hold distance if looking at a table or wall
            float targetDistance = _currentHoldDistance;
            Ray sweepRay = new Ray(camPos, camForward);
            if (Physics.SphereCast(sweepRay, _heldObjectRadius * 0.75f, out RaycastHit obstacleHit, _currentHoldDistance, obstacleLayerMask, QueryTriggerInteraction.Ignore))
            {
                if (obstacleHit.collider.gameObject != _heldRigidbody.gameObject &&
                    obstacleHit.collider != playerCollider &&
                    !obstacleHit.collider.transform.IsChildOf(_heldRigidbody.transform))
                {
                    float safeDist = obstacleHit.distance - surfaceClearancePadding;
                    targetDistance = Mathf.Max(minHoldDistance, safeDist);
                }
            }

            // Smooth distance changes so crosshair distance never snaps abruptly
            _currentSmoothedDistance = Mathf.Lerp(_currentSmoothedDistance, targetDistance, Time.deltaTime * 14f);

            Vector3 idealTargetPos = camPos + camForward * _currentSmoothedDistance;

            // 2. Downward Table Clearance: keep object comfortably above horizontal surfaces
            Ray downCheck = new Ray(idealTargetPos + Vector3.up * 0.25f, Vector3.down);
            if (Physics.Raycast(downCheck, out RaycastHit tableHit, 0.5f, obstacleLayerMask, QueryTriggerInteraction.Ignore))
            {
                if (tableHit.collider.gameObject != _heldRigidbody.gameObject &&
                    tableHit.collider != playerCollider &&
                    !tableHit.collider.transform.IsChildOf(_heldRigidbody.transform))
                {
                    float minAllowedY = tableHit.point.y + _heldObjectRadius + surfaceClearancePadding;
                    if (idealTargetPos.y < minAllowedY)
                    {
                        idealTargetPos.y = Mathf.Lerp(idealTargetPos.y, minAllowedY, Time.deltaTime * 18f);
                    }
                }
            }

            // 3. Smooth Pickup Interpolation
            Vector3 rawAnchorPos = idealTargetPos;
            if (_isPickupSmoothing)
            {
                _pickupTimer += Time.deltaTime;
                float progress = Mathf.Clamp01(_pickupTimer / pickupSmoothDuration);
                float eased = 1f - Mathf.Pow(1f - progress, 3f);

                rawAnchorPos = Vector3.Lerp(_pickupStartWorldPos, idealTargetPos, eased);

                if (progress >= 1f)
                {
                    _isPickupSmoothing = false;
                }
            }

            // 4. Smooth Target Position (SmoothDamp eliminates camera tracking micro-stutters!)
            _smoothedHoldTargetPos = Vector3.SmoothDamp(_smoothedHoldTargetPos, rawAnchorPos, ref _posSmoothVelocity, followSmoothTime);

            // 5. Smooth Yaw Rotation (eliminates angular judder while turning camera or pressing R)
            float desiredYaw = playerCamera.transform.eulerAngles.y + _heldYAngleOffset;
            _currentYaw = Mathf.SmoothDampAngle(_currentYaw, desiredYaw, ref _yawSmoothVelocity, rotateSmoothTime);
        }

        /// <summary>
        /// Runs in FixedUpdate() to apply physics forces and collision checks.
        /// </summary>
        private void UpdateHeldObjectPhysics()
        {
            if (!IsHoldingObject) return;

            Quaternion targetRot = Quaternion.Euler(0f, _currentYaw, 0f);

            // Directly query the true geometric center of the object's colliders in world space
            Vector3 currentColliderCenter = GetRealColliderCenterWorld(_heldRigidbody);

            // 1. Break Distance Check (measured directly from true collider center to crosshair target)
            Vector3 displacement = _smoothedHoldTargetPos - currentColliderCenter;
            if (displacement.sqrMagnitude > breakDistance * breakDistance)
            {
                DropObject();
                return;
            }

            // 2. Apply Smooth Spring Velocity directly tracking collider center to crosshair target
            Vector3 desiredVelocity = displacement * holdSpringForce;
            _heldRigidbody.linearVelocity = Vector3.SmoothDamp(
                _heldRigidbody.linearVelocity,
                Vector3.ClampMagnitude(desiredVelocity, maxHoldSpeed),
                ref _velSmoothVelocity,
                0.02f
            );

            // 3. Apply Smooth Rotation
            _heldRigidbody.MoveRotation(targetRot);
        }

        #endregion

        #region Placement Preview Ray

        private void UpdatePlacementPreview()
        {
            if (!showPlacementPreview || !IsHoldingObject || _heldRigidbody == null)
            {
                HidePlacementPreview();
                return;
            }

            // Drop ray originates from the true center of the collider in world space
            Vector3 rayStart = GetRealColliderCenterWorld(_heldRigidbody);
            Ray dropRay = new Ray(rayStart, Vector3.down);

            if (Physics.Raycast(dropRay, out RaycastHit hit, maxPreviewDropDistance, obstacleLayerMask, QueryTriggerInteraction.Ignore))
            {
                if (hit.collider.gameObject != _heldRigidbody.gameObject &&
                    hit.collider != playerCollider &&
                    !hit.collider.transform.IsChildOf(_heldRigidbody.transform))
                {
                    _previewLineRenderer.enabled = true;
                    _previewLineRenderer.SetPosition(0, rayStart);
                    _previewLineRenderer.SetPosition(1, hit.point);

                    _landingMarkerObj.SetActive(true);
                    _landingMarkerObj.transform.position = hit.point + hit.normal * 0.005f;
                    _landingMarkerObj.transform.rotation = Quaternion.FromToRotation(Vector3.up, hit.normal);
                    return;
                }
            }

            HidePlacementPreview();
        }

        private void HidePlacementPreview()
        {
            if (_previewLineRenderer != null) _previewLineRenderer.enabled = false;
            if (_landingMarkerObj != null) _landingMarkerObj.SetActive(false);
        }

        #endregion

        #region Fallback Physics Management

        private void SetupFallbackGrabPhysics(Rigidbody rb)
        {
            _fallbackCachedGravity = rb.useGravity;
            _fallbackCachedLinearDamping = rb.linearDamping;
            _fallbackCachedAngularDamping = rb.angularDamping;
            _fallbackCachedCollisionMode = rb.collisionDetectionMode;
            _fallbackCachedInterpolation = rb.interpolation;

            rb.useGravity = false;
            rb.linearDamping = 10f;
            rb.angularDamping = 10f;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            rb.interpolation = RigidbodyInterpolation.Interpolate; // Essential for smooth movement!

            if (playerCollider != null)
            {
                var colliders = rb.GetComponentsInChildren<Collider>();
                foreach (var col in colliders)
                {
                    if (col.enabled) Physics.IgnoreCollision(playerCollider, col, true);
                }
            }
        }

        private void RestoreFallbackGrabPhysics(Vector3 releaseVelocity)
        {
            if (_heldRigidbody == null) return;

            _heldRigidbody.useGravity = _fallbackCachedGravity;
            _heldRigidbody.linearDamping = _fallbackCachedLinearDamping;
            _heldRigidbody.angularDamping = _fallbackCachedAngularDamping;
            _heldRigidbody.collisionDetectionMode = _fallbackCachedCollisionMode;
            _heldRigidbody.interpolation = _fallbackCachedInterpolation;
            _heldRigidbody.linearVelocity = Vector3.ClampMagnitude(releaseVelocity, 3.5f);

            if (playerCollider != null)
            {
                var colliders = _heldRigidbody.GetComponentsInChildren<Collider>();
                foreach (var col in colliders)
                {
                    if (col.enabled) Physics.IgnoreCollision(playerCollider, col, false);
                }
            }
        }

        #endregion

        private void OnDestroy()
        {
            if (_previewLineRenderer != null && _previewLineRenderer.gameObject != null)
            {
                Destroy(_previewLineRenderer.gameObject);
            }
            if (_landingMarkerObj != null)
            {
                Destroy(_landingMarkerObj);
            }
        }
    }
}
