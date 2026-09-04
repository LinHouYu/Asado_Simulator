using System;
using UnityEngine;
using UnityEngine.InputSystem;
using AsadoSimulator.Interaction;

namespace AsadoSimulator.Player
{
    /// <summary>
    /// Modern Unity 6 First-Person Player Controller designed for a kitchen simulator.
    /// Manages:
    /// - Paced kitchen walking, sprinting, and crouching.
    /// - Smooth mouse look with vertical pitch clamping.
    /// - Central interactive crosshair linked with ObjectGrabber.
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

        [Header("Interaction System Reference")]
        [Tooltip("Reference to the ObjectGrabber component. Auto-found if null.")]
        [SerializeField] private ObjectGrabber objectGrabber;

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

        // Crosshair Texture
        private Texture2D _crosshairTexture;

        public Camera PlayerCamera => playerCamera;
        public ObjectGrabber Grabber => objectGrabber;

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

            // Locate ObjectGrabber if not manually wired
            if (objectGrabber == null)
            {
                objectGrabber = GetComponentInChildren<ObjectGrabber>();
                if (objectGrabber == null)
                {
                    objectGrabber = FindAnyObjectByType<ObjectGrabber>();
                }
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

        #region Crosshair UI

        private void OnGUI()
        {
            if (!showCrosshair || Cursor.lockState != CursorLockMode.Locked) return;

            bool isAiming = objectGrabber != null && objectGrabber.IsAimingAtGrabbable;

            Vector2 center = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            Color currentColor = isAiming ? crosshairHoverColor : crosshairIdleColor;
            float currentSize = isAiming ? crosshairSize * 1.5f : crosshairSize;

            GUI.color = currentColor;

            // Draw center reticle dot
            Rect dotRect = new Rect(center.x - currentSize * 0.5f, center.y - currentSize * 0.5f, currentSize, currentSize);
            GUI.DrawTexture(dotRect, _crosshairTexture);

            // If hovering over a grabbable object, draw subtle Portal-style reticle brackets
            if (isAiming)
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
