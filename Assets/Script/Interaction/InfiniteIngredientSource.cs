using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Events;

namespace AsadoSimulator.Interaction
{
    /// <summary>
    /// Infinite Ingredient Spawner (食材母体生成器).
    ///
    /// Usage:
    /// 1. Place an ingredient or an empty GameObject on the kitchen counter in the scene.
    /// 2. Attach this script to it. This object becomes the invisible "Mother Spawner" (母体).
    /// 3. The Mother Spawner destroys its own visual renderers and colliders so it cannot be touched,
    ///    hit by rays, or bumped by other objects. It serves purely as the spawn Transform.
    /// 4. At Start, it spawns a full physical ingredient (with Rigidbody, gravity, collider, grabbable).
    /// 5. When the player grabs the ingredient and takes it away, the Mother waits for replenishDelay,
    ///    then spawns a fresh new ingredient at the exact table spot.
    /// </summary>
    [DisallowMultipleComponent]
    public class InfiniteIngredientSource : MonoBehaviour
    {
        public enum ScaleMode
        {
            [Tooltip("Use the exact scale configured in the original Prefab asset (recommended: Chorizo=0.275, Beef=1.0).")]
            UsePrefabScale,
            [Tooltip("Use this Spawner Transform's localScale.")]
            UseSpawnerScale
        }

        [Header("食材预制体 (从 Project 视窗拖入 Prefab)")]
        [Tooltip("要无限产出的食材 Prefab (如 Carne_Asado.prefab, Chorizo_Sausage.prefab, Matambre_Flank_Steak.prefab)。")]
        [SerializeField] private GameObject ingredientPrefab;

        [Header("缩放设置")]
        [Tooltip("生成食材的缩放大小。默认 UsePrefabScale 保证香肠 (0.275) 和牛肉 (1.0) 大小完全正确。")]
        [SerializeField] private ScaleMode scaleMode = ScaleMode.UsePrefabScale;

        [Header("补货时机")]
        [Tooltip("食材被玩家抓起后，隔多少秒原位生成下一个全新食材。")]
        [SerializeField] private float replenishDelay = 0.35f;

        [Header("事件回调 (可选)")]
        public UnityEvent onReplenished;

        // Runtime state
        private GameObject _currentIngredient;
        private GrabbableObject _currentGrabbable;
        private Coroutine _replenishCoroutine;
        private bool _isReplenishing;

        public GameObject IngredientPrefab
        {
            get => ingredientPrefab;
            set => ingredientPrefab = value;
        }

        private void Awake()
        {
            // Auto-resolve prefab if left empty in Inspector
            ResolvePrefabIfNull();

            // Clean Mother: Destroy all colliders, rigidbodies, and renderers on this mother object
            // so nothing can EVER collide with it, bump it, or click it. It is purely a Transform coordinate anchor!
            CleanMotherHost();
        }

        private void CleanMotherHost()
        {
            // 1. Destroy all colliders on the mother so it is physically 100% intangible
            var hostColliders = GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < hostColliders.Length; i++)
            {
                Destroy(hostColliders[i]);
            }

            // 2. Destroy Rigidbody on the mother so it does not fall or move
            var hostRbs = GetComponentsInChildren<Rigidbody>(true);
            for (int i = 0; i < hostRbs.Length; i++)
            {
                Destroy(hostRbs[i]);
            }

            // 3. Destroy GrabbableObject on the mother
            var hostGrabbables = GetComponentsInChildren<GrabbableObject>(true);
            for (int i = 0; i < hostGrabbables.Length; i++)
            {
                Destroy(hostGrabbables[i]);
            }

            // 4. Disable all renderers on the mother so it is 100% invisible
            var hostRenderers = GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < hostRenderers.Length; i++)
            {
                hostRenderers[i].enabled = false;
            }
        }

        private void Start()
        {
            if (ingredientPrefab == null)
            {
                ResolvePrefabIfNull();
                if (ingredientPrefab == null)
                {
                    Debug.LogWarning($"[InfiniteIngredientSource] 请在 '{name}' 的 Inspector 中指定 'Ingredient Prefab'！", this);
                    return;
                }
            }

            // Spawn the initial active ingredient on the table
            SpawnIngredient();
        }

        private void Update()
        {
            // The spawner only monitors whether the active ingredient was grabbed by the player or destroyed.
            // NO distance-based triggers! Only true grabs or destruction trigger replenishment!
            if (_currentIngredient != null && !_isReplenishing)
            {
                if (_currentGrabbable != null && _currentGrabbable.IsGrabbed)
                {
                    OnIngredientTaken();
                }
            }
            else if (_currentIngredient == null && !_isReplenishing)
            {
                // If the previous ingredient was destroyed (e.g. consumed, sliced), replenish
                OnIngredientTaken();
            }
        }

        private void OnDestroy()
        {
            UnsubscribeCurrentIngredient();
        }

        /// <summary>
        /// Spawns a fresh ingredient from the prefab at this exact position and rotation.
        /// </summary>
        private void SpawnIngredient()
        {
            if (ingredientPrefab == null) return;

            // 1. Instantiate the clean prefab at the mother's exact transform
            GameObject newItem = Instantiate(ingredientPrefab, transform.position, transform.rotation);

            // 2. Determine target scale
            Vector3 targetScale = (scaleMode == ScaleMode.UseSpawnerScale)
                ? transform.localScale
                : ingredientPrefab.transform.localScale;

            newItem.transform.localScale = targetScale;

            // 3. Remove any InfiniteIngredientSource from the spawned child if the prefab had one
            var childSources = newItem.GetComponentsInChildren<InfiniteIngredientSource>(true);
            for (int i = 0; i < childSources.Length; i++)
            {
                DestroyImmediate(childSources[i]);
            }

            // 4. GUARANTEE active visual renderers on the spawned item
            var renderers = newItem.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                renderers[i].enabled = true;
            }

            // 5. GUARANTEE active colliders on the spawned item
            var colliders = newItem.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; i++)
            {
                colliders[i].enabled = true;
            }

            // 6. Guarantee Rigidbody and GrabbableObject components
            if (!newItem.TryGetComponent<Rigidbody>(out var rb))
            {
                rb = newItem.AddComponent<Rigidbody>();
            }

            if (!newItem.TryGetComponent<GrabbableObject>(out var grabbable))
            {
                grabbable = newItem.AddComponent<GrabbableObject>();
            }
            grabbable.enabled = true;
            grabbable.ResetGrabState();

            // 7. Configure the waiting state on the table (Kinematic + Trigger)
            // Clear velocities while dynamic (NO kinematic velocity error!)
            if (!rb.isKinematic)
            {
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }
            // Freeze in place on table so it never falls or gets pushed
            rb.isKinematic = true;
            rb.useGravity = false;
            // Set trigger so other objects pass through it without colliding
            grabbable.SetCollidersTrigger(true);

            // Hook pickup event
            _currentGrabbable = grabbable;
            _currentGrabbable.OnGrabbedAction += HandleIngredientGrabbedAction;

            _currentIngredient = newItem;
            _isReplenishing = false;

            onReplenished?.Invoke();
        }

        private void HandleIngredientGrabbedAction(GrabbableObject grabbedObj)
        {
            OnIngredientTaken();
        }

        /// <summary>
        /// Triggered ONLY when the current ingredient is actually picked up by the player.
        /// </summary>
        private void OnIngredientTaken()
        {
            if (_isReplenishing) return;
            _isReplenishing = true;

            UnsubscribeCurrentIngredient();
            _currentIngredient = null;
            _currentGrabbable = null;

            // Start replenish countdown
            if (_replenishCoroutine != null) StopCoroutine(_replenishCoroutine);
            _replenishCoroutine = StartCoroutine(ReplenishRoutine());
        }

        private void UnsubscribeCurrentIngredient()
        {
            if (_currentGrabbable != null)
            {
                _currentGrabbable.OnGrabbedAction -= HandleIngredientGrabbedAction;
            }
        }

        private IEnumerator ReplenishRoutine()
        {
            // Wait for delay (during this time, table spot is empty: "母体不显示，等待新的食材生成出来")
            if (replenishDelay > 0f)
            {
                yield return new WaitForSeconds(replenishDelay);
            }

            // Spawn the new replacement ingredient
            SpawnIngredient();
            _replenishCoroutine = null;
        }

        private void ResolvePrefabIfNull()
        {
            if (ingredientPrefab != null) return;

            #if UNITY_EDITOR
            // 1. Try to get source prefab of this GameObject
            var source = UnityEditor.PrefabUtility.GetCorrespondingObjectFromSource(gameObject);
            if (source != null)
            {
                ingredientPrefab = source;
                return;
            }

            // 2. Try to match by name in project Assets/Prefab
            string cleanName = gameObject.name.Replace("(Clone)", "").Trim();
            string[] guids = UnityEditor.AssetDatabase.FindAssets(cleanName + " t:Prefab");
            for (int i = 0; i < guids.Length; i++)
            {
                string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guids[i]);
                if (path.Contains("Prefab") || path.Contains("comida"))
                {
                    var loaded = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    if (loaded != null)
                    {
                        ingredientPrefab = loaded;
                        return;
                    }
                }
            }
            #endif
        }

        #if UNITY_EDITOR
        private void OnValidate()
        {
            ResolvePrefabIfNull();
        }

        private void OnDrawGizmos()
        {
            Gizmos.color = new Color(0.1f, 0.85f, 1f, 0.7f);
            Gizmos.DrawWireSphere(transform.position, 0.08f);
            Gizmos.color = Color.cyan;
            Gizmos.DrawRay(transform.position, transform.forward * 0.2f);
        }
        #endif
    }
}
