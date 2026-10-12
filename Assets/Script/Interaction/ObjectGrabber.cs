using System;
using UnityEngine;
using UnityEngine.InputSystem;
using AsadoSimulator.Multiplayer;

namespace AsadoSimulator.Interaction
{
    [DisallowMultipleComponent]
    public class ObjectGrabber : MonoBehaviour
    {
        [Header("Camera & References")]
        [SerializeField] private Camera playerCamera;
        [SerializeField] private Collider playerCollider;

        [Header("Tag & Target Filtering")]
        [SerializeField] private string targetTag = "Grabbable";
        [SerializeField] private bool requireTagMatch = true;
        [SerializeField] private float grabRange = 2.8f;
        [SerializeField] private LayerMask grabLayerMask = ~0;

        [Header("Hold & Smooth Settings")]
        [SerializeField] private float defaultHoldDistance = 1.6f;
        [SerializeField] private float minHoldDistance = 0.8f;
        [SerializeField] private float pickupSmoothDuration = 0.22f;
        [Range(0.01f, 0.15f)][SerializeField] private float followSmoothTime = 0.035f;
        [Range(0.01f, 0.15f)][SerializeField] private float rotateSmoothTime = 0.04f;
        [SerializeField] private float holdSpringForce = 22f;
        [SerializeField] private float maxHoldSpeed = 12f;
        [SerializeField] private float breakDistance = 2.4f;

        [Header("Obstacle Layers")]
        [SerializeField] private LayerMask obstacleLayerMask = ~0;

        [Header("Rotation Settings (R Key)")]
        [SerializeField] private float rKeyRotateSpeed = 135f;

        [Header("Placement Preview (Landing Red Dot)")]
        [SerializeField] private bool showPlacementPreview = true;
        [SerializeField] private float maxPreviewDropDistance = 4f;
        [SerializeField] private Color landingMarkerColor = new Color(1f, 0.1f, 0.1f, 0.95f);
        [SerializeField] private float landingMarkerSize = 0.05f;

        // State Tracking
        private Rigidbody _heldRigidbody;
        private GrabbableObject _heldGrabbable;
        private Collider[] _heldColliders;
        private float _currentHoldDistance;
        private float _heldObjectRadius = 0.2f;
        
        // --- 新增：用于跟踪当前注视/抓取的带有高亮组件的物体 ---
        private ItemHighlight _currentHoveredItem;
        private ItemHighlight _heldItem;

        private Vector3 _smoothedHoldTargetPos;
        private Vector3 _posSmoothVelocity;
        private Vector3 _velSmoothVelocity;
        private float _currentSmoothedDistance;
        private float _currentYaw;
        private float _yawSmoothVelocity;
        private bool _isPickupSmoothing;
        private float _pickupTimer;
        private Vector3 _pickupStartWorldPos;
        private float _heldYAngleOffset;

        private bool _fallbackCachedGravity;
        private float _fallbackCachedLinearDamping;
        private float _fallbackCachedAngularDamping;
        private CollisionDetectionMode _fallbackCachedCollisionMode;
        private RigidbodyInterpolation _fallbackCachedInterpolation;
        private float _fallbackCachedMaxDepenetration;
        private float _fallbackCachedMaxAngularVelocity;
        private int _fallbackCachedSolverIterations;
        private int _fallbackCachedSolverVelocityIterations;

        private GameObject _landingMarkerObj;
        private MeshRenderer _landingMarkerRenderer;

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
            if (playerCamera == null) playerCamera = Camera.main;
        }

        #region Hover & Input

        private void UpdateRaycastHoverState()
        {
            IsAimingAtGrabbable = false;
            ItemHighlight newHoveredItem = null;

            if (playerCamera != null && !IsHoldingObject)
            {
                if (RaycastForGrabbable(out RaycastHit hit))
                {
                    IsAimingAtGrabbable = true;
                    // 尝试获取目标物体上的高亮组件
                    newHoveredItem = hit.collider.GetComponentInParent<ItemHighlight>();
                }
            }

            // 处理视线移入/移出逻辑
            if (newHoveredItem != _currentHoveredItem)
            {
                // 移开上一个物体时，清除悬停状态
                if (_currentHoveredItem != null)
                {
                    _currentHoveredItem.SetHover(false);
                }
                
                _currentHoveredItem = newHoveredItem;
                
                // 看向新物体时，设置悬停状态
                if (_currentHoveredItem != null)
                {
                    _currentHoveredItem.SetHover(true);
                }
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
            try { return obj.CompareTag(tagToCompare); }
            catch { return obj.tag == tagToCompare; }
        }

        private void HandleInput()
        {
            var mouse = Mouse.current;
            if (mouse == null) return;
            if (mouse.leftButton.wasPressedThisFrame && !IsHoldingObject) TryGrabObject();
            if (mouse.rightButton.wasPressedThisFrame && IsHoldingObject) DropObject();
        }

        private void HandleRotationInput()
        {
            if (!IsHoldingObject) return;
            var keyboard = Keyboard.current;
            if (keyboard == null) return;
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
                NetworkSyncObject.NotifyGrab(_heldGrabbable.gameObject);
            }
            else if (hitObject.TryGetComponent<Rigidbody>(out var rb) || (rb = hitObject.GetComponentInParent<Rigidbody>()) != null)
            {
                _heldGrabbable = null;
                _heldRigidbody = rb;
                _currentHoldDistance = Mathf.Max(minHoldDistance, defaultHoldDistance);
                SetupFallbackGrabPhysics(rb);
                NetworkSyncObject.NotifyGrab(rb.gameObject);
            }
            else
            {
                return;
            }

            if (_heldRigidbody == null) return;

            // --- 新增：通知高亮组件已被抓取，锁定状态 ---
            _heldItem = _heldRigidbody.GetComponent<ItemHighlight>();
            if (_heldItem != null)
            {
                _heldItem.SetGrabbed(true);
            }

            Vector3 initialColliderCenter = GetRealColliderCenterWorld(_heldRigidbody);
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

            _isPickupSmoothing = true;
            _pickupTimer = 0f;
            _pickupStartWorldPos = initialColliderCenter;
            _smoothedHoldTargetPos = initialColliderCenter;
            _posSmoothVelocity = Vector3.zero;
            _velSmoothVelocity = Vector3.zero;
            _currentSmoothedDistance = _currentHoldDistance;

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
            GameObject droppedObj = _heldGrabbable != null ? _heldGrabbable.gameObject : (_heldRigidbody != null ? _heldRigidbody.gameObject : null);

            if (_heldGrabbable != null) _heldGrabbable.OnDrop(releaseVelocity);
            else RestoreFallbackGrabPhysics(releaseVelocity);

            if (droppedObj != null) NetworkSyncObject.NotifyDrop(droppedObj, releaseVelocity);

            // --- 新增：解除高亮锁定，如果准星还在上面则恢复悬停 ---
            if (_heldItem != null)
            {
                _heldItem.SetGrabbed(false);
                // 强制更新一次当前悬停目标以防止状态卡住
                if (_heldItem == _currentHoveredItem)
                {
                    _heldItem.SetHover(true);
                }
                _heldItem = null;
            }

            _heldRigidbody = null;
            _heldGrabbable = null;
            _heldColliders = null;
            _isPickupSmoothing = false;
            HidePlacementPreview();
        }

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
                if (!hasBounds) { combinedBounds = col.bounds; hasBounds = true; }
                else combinedBounds.Encapsulate(col.bounds);
            }
            return hasBounds ? combinedBounds.center : rb.position;
        }

        #endregion

        #region Ultra-Smooth Hold & Physics

        private void ComputeSmoothHoldTarget()
        {
            if (!IsHoldingObject || playerCamera == null) return;
            Vector3 camPos = playerCamera.transform.position;
            Vector3 camForward = playerCamera.transform.forward;
            Vector3 idealTargetPos = camPos + camForward * _currentHoldDistance;
            Vector3 rawAnchorPos = idealTargetPos;

            if (_isPickupSmoothing)
            {
                _pickupTimer += Time.deltaTime;
                float progress = Mathf.Clamp01(_pickupTimer / pickupSmoothDuration);
                float eased = 1f - Mathf.Pow(1f - progress, 3f);
                rawAnchorPos = Vector3.Lerp(_pickupStartWorldPos, idealTargetPos, eased);
                if (progress >= 1f) _isPickupSmoothing = false;
            }

            _smoothedHoldTargetPos = Vector3.SmoothDamp(_smoothedHoldTargetPos, rawAnchorPos, ref _posSmoothVelocity, followSmoothTime);
            float desiredYaw = playerCamera.transform.eulerAngles.y + _heldYAngleOffset;
            _currentYaw = Mathf.SmoothDampAngle(_currentYaw, desiredYaw, ref _yawSmoothVelocity, rotateSmoothTime);

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

        private void UpdateHeldObjectPhysics()
        {
            if (!IsHoldingObject || _heldRigidbody == null) return;
            Vector3 currentColliderCenter = GetRealColliderCenterWorld(_heldRigidbody);
            Vector3 displacement = _smoothedHoldTargetPos - currentColliderCenter;
            
            if (displacement.sqrMagnitude > breakDistance * breakDistance)
            {
                DropObject();
                return;
            }

            Vector3 desiredVelocity = Vector3.ClampMagnitude(displacement * holdSpringForce, maxHoldSpeed);
            Vector3 currentVelocity = _heldRigidbody.linearVelocity;
            Vector3 linearAccel = (desiredVelocity - currentVelocity) / 0.035f;
            linearAccel = Vector3.ClampMagnitude(linearAccel, 140f);
            _heldRigidbody.AddForce(linearAccel, ForceMode.Acceleration);

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

            Vector3 center = GetRealColliderCenterWorld(_heldRigidbody);
            float bottomY = center.y;
            if (_heldColliders != null)
            {
                for (int i = 0; i < _heldColliders.Length; i++)
                {
                    var c = _heldColliders[i];
                    if (c != null && c.enabled && !c.isTrigger) bottomY = Mathf.Min(bottomY, c.bounds.min.y);
                }
            }

            Vector3 rayStart = new Vector3(center.x, bottomY + 0.005f, center.z);
            Ray dropRay = new Ray(rayStart, Vector3.down);

            if (Physics.Raycast(dropRay, out RaycastHit hit, maxPreviewDropDistance, obstacleLayerMask, QueryTriggerInteraction.Ignore))
            {
                if (hit.collider.gameObject != _heldRigidbody.gameObject && hit.collider != playerCollider && !hit.collider.transform.IsChildOf(_heldRigidbody.transform))
                {
                    _landingMarkerObj.SetActive(true);
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
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.maxDepenetrationVelocity = 1.5f;
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
            if (_landingMarkerObj != null) Destroy(_landingMarkerObj);
        }
    }
}