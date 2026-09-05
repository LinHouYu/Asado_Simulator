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

        [Header("Obstacle Layers")]
        [Tooltip("Layers treated as solid obstacles for landing marker.")]
        [SerializeField] private LayerMask obstacleLayerMask = ~0;

        [Header("Rotation Settings (R Key)")]
        [Tooltip("Speed in degrees per second for rotating the held object around Y axis.")]
        [SerializeField] private float rKeyRotateSpeed = 135f;

        [Header("Placement Preview (Landing Red Dot)")]
        [Tooltip("Whether to display the drop landing preview red dot on colliders below.")]
        [SerializeField] private bool showPlacementPreview = true;

        [Tooltip("Maximum downward raycast distance for landing preview.")]
        [SerializeField] private float maxPreviewDropDistance = 4f;

        [Tooltip("Color of the landing target dot (Default: Red).")]
        [SerializeField] private Color landingMarkerColor = new Color(1f, 0.1f, 0.1f, 0.95f);

        [Tooltip("Size/Diameter of the landing red dot marker in meters.")]
        [SerializeField] private float landingMarkerSize = 0.05f;

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
        private float _fallbackCachedMaxDepenetration;
        private float _fallbackCachedMaxAngularVelocity;
        private int _fallbackCachedSolverIterations;
        private int _fallbackCachedSolverVelocityIterations;

        // Placement Preview Components (Small Red Dot on Collider)
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
            // Setup small landing red dot marker on collider surfaces (pure visual mesh, zero collider)
            _landingMarkerObj = new GameObject("LandingMarkerDot");
            _landingMarkerObj.transform.SetParent(transform);
            var meshFilter = _landingMarkerObj.AddComponent<MeshFilter>();
            var tempPrimitive = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            meshFilter.sharedMesh = tempPrimitive.GetComponent<MeshFilter>().sharedMesh;
            DestroyImmediate(tempPrimitive);

            _landingMarkerObj.transform.localScale = new Vector3(landingMarkerSize, 0.001f, landingMarkerSize);
            _landingMarkerRenderer = _landingMarkerObj.AddComponent<MeshRenderer>();
            Shader dotShader = Shader.Find("Universal Render Pipeline/Unlit");
            if (dotShader == null) dotShader = Shader.Find("Sprites/Default");
            _landingMarkerRenderer.material = new Material(dotShader);
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

            if (target.TryGetComponent<GrabbableObject>(out var grabbable) || (grabbable = target.GetComponentInParent<GrabbableObject>()) != null)
            {
                return !requireTagMatch || grabbable.CanGrabWithTag(targetTag);
            }

            if (target.TryGetComponent<Rigidbody>(out var rb) || (rb = target.GetComponentInParent<Rigidbody>()) != null)
            {
                return !requireTagMatch || SafeCompareTag(target, targetTag) || SafeCompareTag(rb.gameObject, targetTag);
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

            if (hitObject.TryGetComponent<GrabbableObject>(out var grabbable) || (grabbable = hitObject.GetComponentInParent<GrabbableObject>()) != null)
            {
                _heldGrabbable = grabbable;
                _heldRigidbody = grabbable.Rigidbody != null ? grabbable.Rigidbody : grabbable.GetComponent<Rigidbody>();
                if (_heldRigidbody == null) _heldRigidbody = grabbable.GetComponentInParent<Rigidbody>();
                if (_heldRigidbody == null) return;

                float rawDist = grabbable.CustomHoldDistance > 0f ? grabbable.CustomHoldDistance : defaultHoldDistance;
                _currentHoldDistance = Mathf.Max(minHoldDistance, rawDist);
                _heldGrabbable.OnGrab(playerCollider);
            }
            else if (hitObject.TryGetComponent<Rigidbody>(out var rb) || (rb = hitObject.GetComponentInParent<Rigidbody>()) != null)
            {
                _heldGrabbable = null;
                _heldRigidbody = rb;
                _currentHoldDistance = Mathf.Max(minHoldDistance, defaultHoldDistance);
                SetupFallbackGrabPhysics(rb);
            }
            else
            {
                return;
            }

            if (_heldRigidbody == null) return;

            // 1. Calculate true world collider center directly from the object's real colliders
            Vector3 initialColliderCenter = GetRealColliderCenterWorld(_heldRigidbody);

            // Measure object bounding radius from the colliders in world space
            _heldColliders = _heldRigidbody.GetComponentsInChildren<Collider>();
            Bounds bounds = new Bounds(initialColliderCenter, Vector3.zero);
            if (_heldColliders != null)
            {
                foreach (var col in _heldColliders)
                {
                    if (col != null && col.enabled && !col.isTrigger) bounds.Encapsulate(col.bounds);
                }
            }
            _heldObjectRadius = Mathf.Clamp(bounds.extents.magnitude * 0.5f, 0.05f, 0.8f);

            // 2. Smooth Pickup setup: start directly from the object's true collider center
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

            // Target position is held directly at the crosshair center coordinate at designated hold distance
            // No surrounding sweep or ground push, preventing jitter/jumping in narrow spaces like grills
            Vector3 idealTargetPos = camPos + camForward * _currentHoldDistance;

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

            // Prevent target yaw from winding up endlessly if physical obstacle blocks rotation
            if (_heldRigidbody != null)
            {
                float currentPhysYaw = _heldRigidbody.rotation.eulerAngles.y;
                float angleLag = Mathf.DeltaAngle(currentPhysYaw, _currentYaw);
                if (Mathf.Abs(angleLag) > 40f)
                {
                    float clampedYaw = currentPhysYaw + Mathf.Sign(angleLag) * 40f;
                    _currentYaw = clampedYaw;
                    _heldYAngleOffset = Mathf.DeltaAngle(playerCamera.transform.eulerAngles.y, clampedYaw);
                }
            }
        }

        /// <summary>
        /// Runs in FixedUpdate() to apply physics forces and collision checks.
        /// Uses PD Force and Torque to ensure realistic collision constraint solving with zero jitter.
        /// </summary>
        private void UpdateHeldObjectPhysics()
        {
            if (!IsHoldingObject || _heldRigidbody == null) return;

            // Query the true geometric center of all colliders in world space
            Vector3 currentColliderCenter = GetRealColliderCenterWorld(_heldRigidbody);

            // 1. Break Distance Check (measured from true collider center to crosshair target)
            Vector3 displacement = _smoothedHoldTargetPos - currentColliderCenter;
            if (displacement.sqrMagnitude > breakDistance * breakDistance)
            {
                DropObject();
                return;
            }

            // 2. Physics PD Linear Acceleration (Participates in PhysX collision solver; cancels out cleanly on collision surfaces)
            Vector3 desiredVelocity = Vector3.ClampMagnitude(displacement * holdSpringForce, maxHoldSpeed);
            Vector3 currentVelocity = _heldRigidbody.linearVelocity;
            Vector3 linearAccel = (desiredVelocity - currentVelocity) / 0.035f;
            linearAccel = Vector3.ClampMagnitude(linearAccel, 140f);
            _heldRigidbody.AddForce(linearAccel, ForceMode.Acceleration);

            // 3. Physics Torque Rotation (Rotates around center of mass; stops naturally at obstacles without violent penetration)
            Quaternion targetRot = Quaternion.Euler(0f, _currentYaw, 0f);
            Quaternion deltaRot = targetRot * Quaternion.Inverse(_heldRigidbody.rotation);
            deltaRot.ToAngleAxis(out float angleInDegrees, out Vector3 axis);
            if (angleInDegrees > 180f) angleInDegrees -= 360f;

            if (!float.IsNaN(axis.x) && !float.IsInfinity(axis.x) && axis.sqrMagnitude > 0.001f && Mathf.Abs(angleInDegrees) > 0.05f)
            {
                float targetRadSpeed = Mathf.Clamp(angleInDegrees * Mathf.Deg2Rad * 18f, -12f, 12f);
                Vector3 targetAngVel = axis.normalized * targetRadSpeed;
                Vector3 angAccel = (targetAngVel - _heldRigidbody.angularVelocity) / 0.04f;
                angAccel = Vector3.ClampMagnitude(angAccel, 160f);
                _heldRigidbody.AddTorque(angAccel, ForceMode.Acceleration);
            }
            else
            {
                Vector3 angDamp = -_heldRigidbody.angularVelocity * 15f;
                angDamp = Vector3.ClampMagnitude(angDamp, 120f);
                _heldRigidbody.AddTorque(angDamp, ForceMode.Acceleration);
            }
        }

        #endregion

        #region Placement Preview (Landing Red Dot)

        private void UpdatePlacementPreview()
        {
            if (!showPlacementPreview || !IsHoldingObject || _heldRigidbody == null)
            {
                HidePlacementPreview();
                return;
            }

            // Drop ray originates from the bottom of the held object's colliders
            Vector3 center = GetRealColliderCenterWorld(_heldRigidbody);
            float bottomY = center.y;
            if (_heldColliders != null)
            {
                for (int i = 0; i < _heldColliders.Length; i++)
                {
                    var c = _heldColliders[i];
                    if (c != null && c.enabled && !c.isTrigger)
                    {
                        bottomY = Mathf.Min(bottomY, c.bounds.min.y);
                    }
                }
            }

            Vector3 rayStart = new Vector3(center.x, bottomY - 0.005f, center.z);
            Ray dropRay = new Ray(rayStart, Vector3.down);

            // Cast downward to hit solid physics Colliders (ignoring Triggers)
            if (Physics.Raycast(dropRay, out RaycastHit hit, maxPreviewDropDistance, obstacleLayerMask, QueryTriggerInteraction.Ignore))
            {
                if (hit.collider.gameObject != _heldRigidbody.gameObject &&
                    hit.collider != playerCollider &&
                    !hit.collider.transform.IsChildOf(_heldRigidbody.transform))
                {
                    _landingMarkerObj.SetActive(true);
                    // Place red dot directly on collider surface with slight offset along normal to prevent Z-fighting
                    _landingMarkerObj.transform.position = hit.point + hit.normal * 0.002f;
                    _landingMarkerObj.transform.rotation = Quaternion.FromToRotation(Vector3.up, hit.normal);
                    return;
                }
            }

            HidePlacementPreview();
        }

        private void HidePlacementPreview()
        {
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
            _fallbackCachedMaxDepenetration = rb.maxDepenetrationVelocity;
            _fallbackCachedMaxAngularVelocity = rb.maxAngularVelocity;
            _fallbackCachedSolverIterations = rb.solverIterations;
            _fallbackCachedSolverVelocityIterations = rb.solverVelocityIterations;

            rb.useGravity = false;
            rb.linearDamping = 10f;
            rb.angularDamping = 10f;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            rb.interpolation = RigidbodyInterpolation.Interpolate; // Essential for smooth movement!
            rb.maxDepenetrationVelocity = 1.5f; // Suppresses explosive flinging
            rb.maxAngularVelocity = 15f;
            rb.solverIterations = 14;
            rb.solverVelocityIterations = 6;

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
            _heldRigidbody.maxDepenetrationVelocity = Mathf.Min(_fallbackCachedMaxDepenetration > 0f ? _fallbackCachedMaxDepenetration : 3.0f, 3.0f);
            _heldRigidbody.maxAngularVelocity = _fallbackCachedMaxAngularVelocity > 0f ? _fallbackCachedMaxAngularVelocity : 7.0f;
            _heldRigidbody.solverIterations = (_fallbackCachedSolverIterations > 0) ? _fallbackCachedSolverIterations : 6;
            _heldRigidbody.solverVelocityIterations = (_fallbackCachedSolverVelocityIterations > 0) ? _fallbackCachedSolverVelocityIterations : 1;
            _heldRigidbody.linearVelocity = Vector3.ClampMagnitude(releaseVelocity, 3.5f);
            _heldRigidbody.angularVelocity = Vector3.ClampMagnitude(_heldRigidbody.angularVelocity, 5.0f);

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
            if (_landingMarkerObj != null)
            {
                Destroy(_landingMarkerObj);
            }
        }
    }
}
