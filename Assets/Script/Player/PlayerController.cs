using System;
using UnityEngine;
using UnityEngine.InputSystem;
using AsadoSimulator.Interaction;

namespace AsadoSimulator.Player
{
    /// <summary>
    /// Modern Unity 6 First-Person Player Controller designed for a kitchen simulator.
    /// Features:
    /// - Kitchen-tuned realistic walking & crouching movement.
    /// - Smooth mouse look with vertical pitch clamp.
    /// - Central interactive crosshair with hover feedback.
    /// - Portal-style physics object grabbing (Left Click to Grab, Right Click to Drop).
    /// - Tag-filtered interaction for kitchen props.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    [DisallowMultipleComponent]
    public class PlayerController : MonoBehaviour
    {
        [Header("Movement Settings (Kitchen Paced)")]
        [Tooltip("Standard walking speed, tuned for kitchen precision.")]
        [SerializeField] private float walkSpeed = 2.8f;

        [Tooltip("Optional brisk walking speed.")]
        [SerializeField] private float sprintSpeed = 4.2f;

        [Tooltip("Movement speed while crouching (useful for lower shelves/ovens).")]
        [SerializeField] private float crouchSpeed = 1.6f;

        [Tooltip("Smooth acceleration & deceleration factor.")]
        [SerializeField] private float acceleration = 12f;

        [Tooltip("Jump force height (0 to disable jump for pure walking sim).")]
        [SerializeField] private float jumpHeight = 0.6f;

        [Tooltip("Downward gravity acceleration.")]
        [SerializeField] private float gravity = -9.81f;

        [Header("Crouch Settings")]
        [Tooltip("Standing height of the CharacterController.")]
        [SerializeField] private float standingHeight = 1.8f;

        [Tooltip("Crouching height of the CharacterController.")]
        [SerializeField] private float crouchHeight = 1.1f;

        [Tooltip("Speed of transition between standing and crouching.")]
        [SerializeField] private float crouchTransitionSpeed = 10f;

        [Header("Camera & Look")]
        [Tooltip("First-person camera. If unassigned, automatically finds child Camera.")]
        [SerializeField] private Camera playerCamera;

        [Tooltip("Mouse look sensitivity.")]
        [SerializeField] private float mouseSensitivity = 1.5f;

        [Tooltip("Minimum vertical pitch angle in degrees.")]
        [SerializeField] private float minPitch = -85f;

        [Tooltip("Maximum vertical pitch angle in degrees.")]
        [SerializeField] private float maxPitch = 85f;

        [Header("Portal-Style Object Grabbing")]
        [Tooltip("Target tag required for an object to be grabbable.")]
        [SerializeField] private string targetTag = "Grabbable";

        [Tooltip("If true, only objects with the matching tag can be picked up.")]
        [SerializeField] private bool requireTagMatch = true;

        [Tooltip("Maximum distance to reach and grab objects.")]
        [SerializeField] private float grabRange = 2.5f;

        [Tooltip("Default distance the held object floats in front of the camera.")]
        [SerializeField] private float defaultHoldDistance = 1.8f;

        [Tooltip("Spring pulling force moving the held object to the target point.")]
        [SerializeField] private float holdSpringForce = 18f;

        [Tooltip("Maximum linear velocity of the held object.")]
        [SerializeField] private float maxHoldSpeed = 12f;

        [Tooltip("Rotation alignment speed tracking the camera.")]
        [SerializeField] private float holdRotateSpeed = 12f;

        [Tooltip("Distance threshold where the hold breaks if the object is obstructed.")]
        [SerializeField] private float breakDistance = 2.2f;

        [Tooltip("Layers to raycast against for grabbing.")]
        [SerializeField] private LayerMask grabLayerMask = ~0;

        [Header("Crosshair Settings")]
        [Tooltip("Whether to draw the center screen crosshair.")]
        [SerializeField] private bool showCrosshair = true;

        [Tooltip("Crosshair color in idle state.")]
        [SerializeField] private Color crosshairIdleColor = new Color(1f, 1f, 1f, 0.75f);

        [Tooltip("Crosshair color when aiming at a grabbable object within range.")]
        [SerializeField] private Color crosshairHoverColor = new Color(0.2f, 0.9f, 1f, 0.95f);

        [Tooltip("Crosshair dot size in pixels.")]
        [SerializeField] private float crosshairSize = 4f;

        // References
        private CharacterController _characterController;
        private Transform _cameraTransform;

        // Movement State
        private Vector3 _currentVelocity;
        private float _verticalVelocity;
        private bool _isCrouching;
        private float _cameraStandingLocalY;
        private float _cameraCrouchLocalY;

        // Look State
        private float _cameraPitch;

        // Grab State
        private Rigidbody _heldRigidbody;
        private GrabbableObject _heldGrabbable;
        private float _currentHoldDistance;
        private Quaternion _heldRotationOffset;

        // Grabbing Fallback Physics Cache (when target has no GrabbableObject script)
        private bool _fallbackCachedGravity;
        private float _fallbackCachedLinearDamping;
        private float _fallbackCachedAngularDamping;
        private Collider[] _fallbackColliders;

        // Crosshair Hover State
        private bool _isAimingAtGrabbable;
        private Texture2D _crosshairTexture;

        public bool IsHoldingObject => _heldRigidbody != null;
        public Rigidbody HeldRigidbody => _heldRigidbody;

        private void Awake()
        {
            _characterController = GetComponent<CharacterController>();

            // Disable redundant CapsuleCollider if present to avoid collision jitter with CharacterController
            if (TryGetComponent<CapsuleCollider>(out var capsuleCollider))
            {
                capsuleCollider.enabled = false;
            }

            // Auto-locate camera if not manually wired
            if (playerCamera == null)
            {
                playerCamera = GetComponentInChildren<Camera>();
                if (playerCamera == null)
                {
                    playerCamera = Camera.main;
                }
            }

            if (playerCamera != null)
            {
                // If the camera is at scene root, parent it to player's head automatically
                if (playerCamera.transform.parent != transform)
                {
                    playerCamera.transform.SetParent(transform);
                    playerCamera.transform.localPosition = new Vector3(0f, standingHeight * 0.88f, 0f);
                    playerCamera.transform.localRotation = Quaternion.identity;
                }

                _cameraTransform = playerCamera.transform;
                _cameraStandingLocalY = _cameraTransform.localPosition.y;
                _cameraCrouchLocalY = _cameraStandingLocalY - (standingHeight - crouchHeight);
            }

            // Generate 1x1 white texture for clean OnGUI rendering
            _crosshairTexture = new Texture2D(1, 1);
            _crosshairTexture.SetPixel(0, 0, Color.white);
            _crosshairTexture.Apply();
        }

        private void Start()
        {
            SetCursorLocked(true);
        }

        private void Update()
        {
            HandleCursorLock();
            HandleCameraLook();
            HandleMovement();
            HandleCrouch();
            UpdateRaycastHoverState();
            HandleGrabInput();
        }

        private void FixedUpdate()
        {
            UpdateHeldObjectPhysics();
        }

        #region Cursor Management

        private void HandleCursorLock()
        {
            var keyboard = Keyboard.current;
            var mouse = Mouse.current;

            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
            {
                SetCursorLocked(false);
            }

            if (mouse != null && (mouse.leftButton.wasPressedThisFrame || mouse.rightButton.wasPressedThisFrame))
            {
                if (Cursor.lockState != CursorLockMode.Locked)
                {
                    SetCursorLocked(true);
                }
            }
        }

        private void SetCursorLocked(bool locked)
        {
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }

        #endregion

        #region Movement & Look

        private void HandleCameraLook()
        {
            if (_cameraTransform == null || Cursor.lockState != CursorLockMode.Locked) return;

            var mouse = Mouse.current;
            if (mouse == null) return;

            Vector2 mouseDelta = mouse.delta.ReadValue() * (mouseSensitivity * 0.1f);

            // Horizontal rotation (Yaw applied to player body)
            transform.Rotate(Vector3.up * mouseDelta.x);

            // Vertical rotation (Pitch clamped and applied to camera)
            _cameraPitch = Mathf.Clamp(_cameraPitch - mouseDelta.y, minPitch, maxPitch);
            _cameraTransform.localEulerAngles = new Vector3(_cameraPitch, 0f, 0f);
        }

        private void HandleMovement()
        {
            var keyboard = Keyboard.current;
            Vector2 inputDir = Vector2.zero;

            if (keyboard != null)
            {
                if (keyboard.wKey.isPressed) inputDir.y += 1f;
                if (keyboard.sKey.isPressed) inputDir.y -= 1f;
                if (keyboard.dKey.isPressed) inputDir.x += 1f;
                if (keyboard.aKey.isPressed) inputDir.x -= 1f;
            }

            inputDir = inputDir.normalized;

            // Determine target speed
            float targetSpeed = walkSpeed;
            if (_isCrouching)
            {
                targetSpeed = crouchSpeed;
            }
            else if (keyboard != null && keyboard.leftShiftKey.isPressed)
            {
                targetSpeed = sprintSpeed;
            }

            // World-space movement direction relative to player rotation
            Vector3 moveTarget = (transform.forward * inputDir.y + transform.right * inputDir.x) * targetSpeed;

            // Smooth horizontal velocity
            _currentVelocity = Vector3.MoveTowards(_currentVelocity, moveTarget, acceleration * Time.deltaTime);

            // Vertical gravity & jumping
            if (_characterController.isGrounded)
            {
                _verticalVelocity = -2f; // Slight downward force to stay anchored to slopes/steps

                if (jumpHeight > 0.05f && keyboard != null && keyboard.spaceKey.wasPressedThisFrame && !_isCrouching)
                {
                    _verticalVelocity = Mathf.Sqrt(2f * Mathf.Abs(gravity) * jumpHeight);
                }
            }
            else
            {
                _verticalVelocity += gravity * Time.deltaTime;
            }

            // Combine horizontal & vertical motion
            Vector3 finalMove = _currentVelocity;
            finalMove.y = _verticalVelocity;

            _characterController.Move(finalMove * Time.deltaTime);
        }

        private void HandleCrouch()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null) return;

            // Crouch on LeftCtrl or C
            _isCrouching = keyboard.leftCtrlKey.isPressed || keyboard.cKey.isPressed;

            float targetHeight = _isCrouching ? crouchHeight : standingHeight;
            float targetCenterY = targetHeight * 0.5f;

            _characterController.height = Mathf.Lerp(_characterController.height, targetHeight, crouchTransitionSpeed * Time.deltaTime);
            _characterController.center = new Vector3(0f, Mathf.Lerp(_characterController.center.y, targetCenterY, crouchTransitionSpeed * Time.deltaTime), 0f);

            if (_cameraTransform != null)
            {
                float targetCamY = _isCrouching ? _cameraCrouchLocalY : _cameraStandingLocalY;
                Vector3 camLocalPos = _cameraTransform.localPosition;
                camLocalPos.y = Mathf.Lerp(camLocalPos.y, targetCamY, crouchTransitionSpeed * Time.deltaTime);
                _cameraTransform.localPosition = camLocalPos;
            }
        }

        #endregion

        #region Portal-Style Object Grabbing

        private void UpdateRaycastHoverState()
        {
            _isAimingAtGrabbable = false;

            if (_cameraTransform == null || IsHoldingObject) return;

            Ray ray = new Ray(_cameraTransform.position, _cameraTransform.forward);
            if (Physics.Raycast(ray, out RaycastHit hit, grabRange, grabLayerMask, QueryTriggerInteraction.Ignore))
            {
                _isAimingAtGrabbable = IsTargetGrabbable(hit.collider.gameObject);
            }
        }

        private bool IsTargetGrabbable(GameObject target)
        {
            if (target == null) return false;

            // Check if object has GrabbableObject component
            if (target.TryGetComponent<GrabbableObject>(out var grabbable))
            {
                return !requireTagMatch || grabbable.CanGrabWithTag(targetTag);
            }

            // Check if object has target tag and a Rigidbody
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

        private void HandleGrabInput()
        {
            var mouse = Mouse.current;
            if (mouse == null) return;

            // Left Click -> Pick up object
            if (mouse.leftButton.wasPressedThisFrame)
            {
                if (!IsHoldingObject)
                {
                    TryGrabObject();
                }
            }

            // Right Click -> Drop object
            if (mouse.rightButton.wasPressedThisFrame)
            {
                if (IsHoldingObject)
                {
                    DropObject();
                }
            }
        }

        private void TryGrabObject()
        {
            if (_cameraTransform == null) return;

            Ray ray = new Ray(_cameraTransform.position, _cameraTransform.forward);
            if (!Physics.Raycast(ray, out RaycastHit hit, grabRange, grabLayerMask, QueryTriggerInteraction.Ignore)) return;

            GameObject hitObject = hit.collider.gameObject;
            if (!IsTargetGrabbable(hitObject)) return;

            // Grab candidate has been verified
            if (hitObject.TryGetComponent<GrabbableObject>(out var grabbable))
            {
                _heldGrabbable = grabbable;
                _heldRigidbody = grabbable.Rigidbody;
                _currentHoldDistance = grabbable.CustomHoldDistance > 0f ? grabbable.CustomHoldDistance : defaultHoldDistance;

                // Notify grabbable prop
                _heldGrabbable.OnGrab(_characterController);
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

            // Compute relative rotation offset
            _heldRotationOffset = Quaternion.Inverse(_cameraTransform.rotation) * _heldRigidbody.rotation;
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
        }

        private void UpdateHeldObjectPhysics()
        {
            if (!IsHoldingObject || _cameraTransform == null) return;

            Vector3 targetPosition = _cameraTransform.position + _cameraTransform.forward * _currentHoldDistance;
            Vector3 displacement = targetPosition - _heldRigidbody.position;

            // Portal Break Distance Check (if object is blocked or wedged behind a wall/counter)
            if (displacement.sqrMagnitude > breakDistance * breakDistance)
            {
                DropObject();
                return;
            }

            // Apply spring linear velocity towards hold position (Modern Unity 6 API)
            Vector3 targetVelocity = displacement * holdSpringForce;
            _heldRigidbody.linearVelocity = Vector3.ClampMagnitude(targetVelocity, maxHoldSpeed);

            // Track camera rotation smoothly
            bool shouldTrackRotation = _heldGrabbable == null || _heldGrabbable.TrackCameraRotation;
            if (shouldTrackRotation)
            {
                Quaternion targetRotation = _cameraTransform.rotation * _heldRotationOffset;
                Quaternion deltaRotation = targetRotation * Quaternion.Inverse(_heldRigidbody.rotation);

                deltaRotation.ToAngleAxis(out float angle, out Vector3 axis);
                if (angle > 180f) angle -= 360f;

                if (!float.IsNaN(axis.x) && axis.sqrMagnitude > 0.001f)
                {
                    _heldRigidbody.angularVelocity = axis.normalized * (angle * Mathf.Deg2Rad * holdRotateSpeed);
                }
            }
        }

        #endregion

        #region Fallback Physics Management

        private void SetupFallbackGrabPhysics(Rigidbody rb)
        {
            _fallbackCachedGravity = rb.useGravity;
            _fallbackCachedLinearDamping = rb.linearDamping;
            _fallbackCachedAngularDamping = rb.angularDamping;

            rb.useGravity = false;
            rb.linearDamping = 10f;
            rb.angularDamping = 10f;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

            _fallbackColliders = rb.GetComponentsInChildren<Collider>();
            for (int i = 0; i < _fallbackColliders.Length; i++)
            {
                if (_fallbackColliders[i] != null && _fallbackColliders[i].enabled)
                {
                    Physics.IgnoreCollision(_characterController, _fallbackColliders[i], true);
                }
            }
        }

        private void RestoreFallbackGrabPhysics(Vector3 releaseVelocity)
        {
            if (_heldRigidbody == null) return;

            _heldRigidbody.useGravity = _fallbackCachedGravity;
            _heldRigidbody.linearDamping = _fallbackCachedLinearDamping;
            _heldRigidbody.angularDamping = _fallbackCachedAngularDamping;
            _heldRigidbody.collisionDetectionMode = CollisionDetectionMode.Discrete;
            _heldRigidbody.linearVelocity = Vector3.ClampMagnitude(releaseVelocity, 4.0f);

            if (_fallbackColliders != null)
            {
                for (int i = 0; i < _fallbackColliders.Length; i++)
                {
                    if (_fallbackColliders[i] != null && _fallbackColliders[i].enabled)
                    {
                        Physics.IgnoreCollision(_characterController, _fallbackColliders[i], false);
                    }
                }
                _fallbackColliders = null;
            }
        }

        #endregion

        #region Crosshair UI

        private void OnGUI()
        {
            if (!showCrosshair || Cursor.lockState != CursorLockMode.Locked) return;

            Vector2 center = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            Color currentColor = _isAimingAtGrabbable ? crosshairHoverColor : crosshairIdleColor;
            float currentSize = _isAimingAtGrabbable ? crosshairSize * 1.5f : crosshairSize;

            GUI.color = currentColor;

            // Draw center reticle dot
            Rect dotRect = new Rect(center.x - currentSize * 0.5f, center.y - currentSize * 0.5f, currentSize, currentSize);
            GUI.DrawTexture(dotRect, _crosshairTexture);

            // If hovering over a grabbable object, draw subtle Portal-style reticle brackets
            if (_isAimingAtGrabbable)
            {
                float bracketOffset = currentSize * 2.5f;
                float bracketLength = currentSize * 1.8f;
                float bracketThickness = 2f;

                // Top tick
                GUI.DrawTexture(new Rect(center.x - bracketThickness * 0.5f, center.y - bracketOffset - bracketLength, bracketThickness, bracketLength), _crosshairTexture);
                // Bottom tick
                GUI.DrawTexture(new Rect(center.x - bracketThickness * 0.5f, center.y + bracketOffset, bracketThickness, bracketLength), _crosshairTexture);
                // Left tick
                GUI.DrawTexture(new Rect(center.x - bracketOffset - bracketLength, center.y - bracketThickness * 0.5f, bracketLength, bracketThickness), _crosshairTexture);
                // Right tick
                GUI.DrawTexture(new Rect(center.x + bracketOffset, center.y - bracketThickness * 0.5f, bracketLength, bracketThickness), _crosshairTexture);
            }

            GUI.color = Color.white;
        }

        private void OnDestroy()
        {
            if (_crosshairTexture != null)
            {
                Destroy(_crosshairTexture);
            }
        }

        #endregion
    }
}
