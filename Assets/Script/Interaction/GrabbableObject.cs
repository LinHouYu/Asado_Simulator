using System;
using UnityEngine;
using UnityEngine.Events;

namespace AsadoSimulator.Interaction
{
    /// <summary>
    /// Component attached to objects that can be picked up and held (Portal-style).
    /// Handles tag verification, Rigidbody state caching, and collision management.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    [DisallowMultipleComponent]
    public class GrabbableObject : MonoBehaviour
    {
        [Header("Tag Configuration")]
        [Tooltip("Tag required for this object to be grabbable. Default is 'Grabbable'.")]
        [SerializeField] private string requiredTag = "Grabbable";

        [Tooltip("Whether to validate and warn if the GameObject tag does not match requiredTag.")]
        [SerializeField] private bool enforceTag = true;

        [Header("Hold Settings")]
        [Tooltip("Preferred distance from camera when held (<= 0 uses player default).")]
        [SerializeField] private float customHoldDistance = -1f;

        [Tooltip("If true, the object will keep its grab orientation relative to the camera.")]
        [SerializeField] private bool trackCameraRotation = true;

        [Tooltip("Multiplier for linear damping while held to prevent swinging.")]
        [SerializeField] private float heldDamping = 10f;

        [Tooltip("Optional custom grab center offset in local space. If Vector3.zero, automatically calculates collider center.")]
        [SerializeField] private Vector3 customCenterOffset = Vector3.zero;
        public Vector3 CustomCenterOffset => customCenterOffset;

        [Header("Events")]
        public UnityEvent onGrabbed;
        public UnityEvent onDropped;
        public event Action<GrabbableObject> OnGrabbedAction;

        // Cached physics references & states
        private Rigidbody _rigidbody;
        private Collider[] _colliders;
        private Collider _playerCollider;
        private bool _cachedUseGravity;
        private float _cachedLinearDamping;
        private float _cachedAngularDamping;
        private CollisionDetectionMode _cachedCollisionMode;
        private RigidbodyInterpolation _cachedInterpolation;
        private float _cachedMaxDepenetrationVelocity;
        private float _cachedMaxAngularVelocity;
        private int _cachedSolverIterations;
        private int _cachedSolverVelocityIterations;

        public bool IsGrabbed { get; private set; }
        public string RequiredTag => requiredTag;
        public float CustomHoldDistance => customHoldDistance;
        public bool TrackCameraRotation => trackCameraRotation;
        public Rigidbody Rigidbody => _rigidbody;

        private void Awake()
        {
            _rigidbody = GetComponent<Rigidbody>();
            _colliders = GetComponentsInChildren<Collider>();

            if (_rigidbody != null)
            {
                _cachedUseGravity = _rigidbody.useGravity;
                _cachedLinearDamping = _rigidbody.linearDamping;
                _cachedAngularDamping = _rigidbody.angularDamping;
                _cachedCollisionMode = _rigidbody.collisionDetectionMode;
                _cachedInterpolation = _rigidbody.interpolation;
                _cachedMaxDepenetrationVelocity = _rigidbody.maxDepenetrationVelocity;
                _cachedMaxAngularVelocity = _rigidbody.maxAngularVelocity;
                _cachedSolverIterations = _rigidbody.solverIterations;
                _cachedSolverVelocityIterations = _rigidbody.solverVelocityIterations;
            }
            else
            {
                _cachedUseGravity = true;
            }

            ValidateTag();
        }

        private void OnValidate()
        {
            ValidateTag();
        }

        private void ValidateTag()
        {
            if (!enforceTag || string.IsNullOrEmpty(requiredTag)) return;

            if (!MatchesTag(requiredTag))
            {
                #if UNITY_EDITOR
                // In Editor, hint developer if needed
                #endif
            }
        }

        public bool MatchesTag(string tagToCheck)
        {
            if (string.IsNullOrEmpty(tagToCheck)) return true;
            try
            {
                return CompareTag(tagToCheck);
            }
            catch
            {
                return gameObject.tag == tagToCheck;
            }
        }

        /// <summary>
        /// Validates if this object can currently be grabbed given a requested tag.
        /// </summary>
        public bool CanGrabWithTag(string tagFilter)
        {
            if (string.IsNullOrEmpty(tagFilter)) return true;
            return MatchesTag(tagFilter) || MatchesTag(requiredTag);
        }

        /// <summary>
        /// Called when the player grabs this object.
        /// </summary>
        public void OnGrab(Collider playerCollider)
        {
            if (IsGrabbed) return;

            IsGrabbed = true;
            _playerCollider = playerCollider;

            // Cache original physics states
            _cachedUseGravity = _rigidbody.useGravity;
            _cachedLinearDamping = _rigidbody.linearDamping;
            _cachedAngularDamping = _rigidbody.angularDamping;
            _cachedCollisionMode = _rigidbody.collisionDetectionMode;
            _cachedInterpolation = _rigidbody.interpolation;

            // Configure physics for stable holding (Interpolate ensures silky-smooth rendering)
            _rigidbody.isKinematic = false;
            _rigidbody.useGravity = false;
            _rigidbody.linearDamping = heldDamping;
            _rigidbody.angularDamping = heldDamping;
            _rigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            _rigidbody.interpolation = RigidbodyInterpolation.Interpolate;
            _rigidbody.maxDepenetrationVelocity = 1.5f; // Critical: suppresses explosive depenetration flinging
            _rigidbody.maxAngularVelocity = 15f; // Prevents wild spinning
            _rigidbody.solverIterations = 14; // High precision contact solver
            _rigidbody.solverVelocityIterations = 6;

            // Turn off trigger mode on grab so the held object is solid
            SetCollidersTrigger(false);

            // Ignore collision with player so held object doesn't push the player
            IgnorePlayerCollision(true);

            onGrabbed?.Invoke();
            OnGrabbedAction?.Invoke(this);
        }

        /// <summary>
        /// Called when the player releases or drops this object.
        /// </summary>
        public void OnDrop(Vector3 releaseLinearVelocity)
        {
            if (!IsGrabbed) return;

            IsGrabbed = false;

            // Restore physics states (always dynamic with gravity and solid collision)
            _rigidbody.isKinematic = false;
            _rigidbody.useGravity = true;
            _rigidbody.linearDamping = _cachedLinearDamping;
            _rigidbody.angularDamping = _cachedAngularDamping;
            _rigidbody.collisionDetectionMode = (_cachedCollisionMode != 0) ? _cachedCollisionMode : CollisionDetectionMode.Discrete;
            _rigidbody.interpolation = _cachedInterpolation;
            _rigidbody.maxDepenetrationVelocity = Mathf.Min(_cachedMaxDepenetrationVelocity > 0f ? _cachedMaxDepenetrationVelocity : 3.0f, 3.0f);
            _rigidbody.maxAngularVelocity = _cachedMaxAngularVelocity > 0f ? _cachedMaxAngularVelocity : 7.0f;
            _rigidbody.solverIterations = (_cachedSolverIterations > 0) ? _cachedSolverIterations : 6;
            _rigidbody.solverVelocityIterations = (_cachedSolverVelocityIterations > 0) ? _cachedSolverVelocityIterations : 1;

            // Ensure colliders remain solid
            SetCollidersTrigger(false);

            // Restore player collision
            IgnorePlayerCollision(false);
            _playerCollider = null;

            // Apply smooth release velocity (clamped to avoid prop launch glitches)
            _rigidbody.linearVelocity = Vector3.ClampMagnitude(releaseLinearVelocity, 3.5f);
            _rigidbody.angularVelocity = Vector3.ClampMagnitude(_rigidbody.angularVelocity, 5.0f);

            onDropped?.Invoke();
        }

        private void IgnorePlayerCollision(bool ignore)
        {
            if (_playerCollider == null || _colliders == null) return;

            for (int i = 0; i < _colliders.Length; i++)
            {
                if (_colliders[i] != null && _colliders[i].enabled)
                {
                    Physics.IgnoreCollision(_playerCollider, _colliders[i], ignore);
                }
            }
        }

        private void OnDisable()
        {
            if (IsGrabbed)
            {
                OnDrop(Vector3.zero);
            }
        }

        /// <summary>
        /// Resets the grabbable object to its initial ungrabbed state.
        /// Useful when cloning or respawning food items.
        /// </summary>
        public void ResetGrabState()
        {
            IsGrabbed = false;
            if (_rigidbody != null)
            {
                _rigidbody.useGravity = true;
                _rigidbody.isKinematic = false;
                _rigidbody.linearDamping = _cachedLinearDamping;
                _rigidbody.angularDamping = _cachedAngularDamping;
                _rigidbody.collisionDetectionMode = (_cachedCollisionMode != 0) ? _cachedCollisionMode : CollisionDetectionMode.Discrete;
                _rigidbody.interpolation = _cachedInterpolation;
                _rigidbody.linearVelocity = Vector3.zero;
                _rigidbody.angularVelocity = Vector3.zero;
            }
            IgnorePlayerCollision(false);
            _playerCollider = null;
        }

        /// <summary>
        /// Sets all colliders on this object and its children to trigger mode or solid mode.
        /// </summary>
        public void SetCollidersTrigger(bool isTrigger)
        {
            if (_colliders == null || _colliders.Length == 0)
            {
                _colliders = GetComponentsInChildren<Collider>(true);
            }

            for (int i = 0; i < _colliders.Length; i++)
            {
                if (_colliders[i] != null)
                {
                    _colliders[i].isTrigger = isTrigger;
                }
            }
        }
    }
}
