using Unity.XR.CoreUtils;
using UnityEngine;

namespace XRFixIt.XR
{
    /// <summary>
    /// Keeps the head (camera) out of solid colliders no matter what moves it: the XR Interaction
    /// Simulator, teleportation, or a real headset. The CharacterController only covers walking,
    /// so anything else that moves the camera would otherwise pass straight through walls and furniture.
    /// Works by sweeping the head from its last safe position and pushing the XR Origin back,
    /// so the head always stops on the side it came from.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(1000)]
    public class HeadCollisionBlocker : MonoBehaviour
    {
        [SerializeField] private XROrigin xrOrigin;
        [SerializeField] private ChairSeatController chair;
        [SerializeField] private float headRadius = 0.15f;
        [SerializeField] private int maxIterations = 4;
        [Tooltip("A single-frame head move longer than this is treated as a teleport, not swept.")]
        [SerializeField] private float teleportDistance = 0.5f;

        private readonly Collider[] overlaps = new Collider[16];
        private readonly RaycastHit[] castHits = new RaycastHit[16];
        private SphereCollider probe;
        private Vector3 lastSafeHead;
        private bool hasSafeHead;

        private void Awake()
        {
            if (xrOrigin == null) xrOrigin = GetComponent<XROrigin>();
            if (chair == null) chair = FindAnyObjectByType<ChairSeatController>();

            // Shape for Physics.ComputePenetration only. Trigger on Ignore Raycast, so it never blocks anything.
            var go = new GameObject("HeadCollisionProbe") { hideFlags = HideFlags.HideAndDontSave, layer = 2 };
            probe = go.AddComponent<SphereCollider>();
            probe.isTrigger = true;
            probe.radius = headRadius;
        }

        private void OnEnable() => Application.onBeforeRender += Resolve;

        private void OnDisable() => Application.onBeforeRender -= Resolve;

        private void OnDestroy()
        {
            if (probe != null) Destroy(probe.gameObject);
        }

        private void LateUpdate() => Resolve();

        private bool IsIgnored(Collider other)
        {
            if (other.transform.IsChildOf(transform)) return true; // the rig's own colliders
            // The chair you are sitting on must not push you off it.
            return chair != null && chair.IsSeated && other.transform.IsChildOf(chair.transform);
        }

        private void Resolve()
        {
            if (xrOrigin == null || xrOrigin.Camera == null || probe == null) return;

            Vector3 head = xrOrigin.Camera.transform.position;
            Vector3 total = Vector3.zero;

            // 1) Sweep from the last safe spot so a fast move stops at the surface it hit, on the near side.
            if (hasSafeHead)
            {
                Vector3 delta = head - lastSafeHead;
                float distance = delta.magnitude;
                if (distance > 1e-4f && distance < teleportDistance)
                {
                    Vector3 direction = delta / distance;
                    int count = Physics.SphereCastNonAlloc(lastSafeHead, headRadius, direction, castHits, distance, ~0, QueryTriggerInteraction.Ignore);
                    float nearest = distance;
                    RaycastHit best = default;
                    bool found = false;
                    for (int i = 0; i < count; i++)
                    {
                        RaycastHit hit = castHits[i];
                        if (hit.distance <= 0f || hit.distance >= nearest || IsIgnored(hit.collider)) continue;
                        nearest = hit.distance;
                        best = hit;
                        found = true;
                    }

                    if (found)
                    {
                        Vector3 stop = lastSafeHead + direction * nearest;
                        Vector3 slide = Vector3.ProjectOnPlane(head - stop, best.normal); // slide along the surface
                        Vector3 resolved = stop + slide;
                        resolved.y = head.y; // only sideways; never launch the player up or down
                        total = resolved - head;
                    }
                }
            }

            // 2) Safety net: anything still overlapping (spawn, teleport, sliding into a corner) is pushed out.
            for (int iteration = 0; iteration < maxIterations; iteration++)
            {
                int count = Physics.OverlapSphereNonAlloc(head + total, headRadius, overlaps, ~0, QueryTriggerInteraction.Ignore);
                bool moved = false;
                for (int i = 0; i < count; i++)
                {
                    Collider other = overlaps[i];
                    if (IsIgnored(other)) continue;

                    // shortcut: ComputePenetration ignores non-convex MeshColliders; fine while the room uses boxes.
                    if (!Physics.ComputePenetration(probe, head + total, Quaternion.identity,
                            other, other.transform.position, other.transform.rotation,
                            out Vector3 direction, out float distance))
                    {
                        continue;
                    }

                    direction.y = 0f;
                    if (direction.sqrMagnitude < 1e-4f) continue;
                    total += direction.normalized * distance;
                    moved = true;
                }
                if (!moved) break;
            }

            if (total.sqrMagnitude > 0f)
            {
                transform.position += total;
                Physics.SyncTransforms(); // so an enabled CharacterController notices the move
            }

            lastSafeHead = head + total;
            hasSafeHead = true;
        }
    }
}
