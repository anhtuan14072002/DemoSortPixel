using System;                        
using System.Collections.Generic;     
using UnityEngine;                 
using UnityEngine.Splines;          
using TMPro;                         
using PrimeTween;                  
using Zenject;                      

namespace Pixel
{
    public class BlockSplineRunner : MonoBehaviour, IPoolable
    {
        // Điểm spawn projectile
        [SerializeField] private Transform projectileSpawnPoint;

        // LayerMask để raycast chỉ trúng các object mong muốn
        [SerializeField] private LayerMask cellLayerMask = ~0;

        // Prefab projectile dùng để bắn cell
        [SerializeField] private GameObject hitPrefab;

        // Text hiển thị số đạn còn lại
        [SerializeField] private TMP_Text remainingShotsText;

        // Sai số cho phép khi so màu block với màu cell
        [SerializeField] private float colorTolerance = 0.05f;

        // Độ dài tia raycast từ block bắn vào map
        [SerializeField] private float castDistance = 8f;

        // Lùi điểm bắt đầu ray ra sau một chút để ray ổn định hơn
        [SerializeField] private float originBackOffset = 0.15f;

        // Tốc độ bay của projectile
        [SerializeField] private float projectileSpeed = 12f;

        // Khoảng nghỉ giữa 2 lần bắn
        [SerializeField] private float shootCooldown = 0.03f;

        // Thời gian scale nhỏ lại khi block hết đạn
        [SerializeField] private float outOfShotsDestroyDuration = 0.15f;

        // Thời gian block nhảy về slot
        [SerializeField] private float returnJumpDuration = 0.35f;

        // Độ cao cú nhảy khi block quay về slot
        [SerializeField] private float returnJumpHeight = 0.6f;

        // Giới hạn số block được chạy cùng lúc
        [SerializeField] private int maxConcurrentRunningBlocks = 5;

        // Khoảng cách tối thiểu giữa block này với block đang chạy khác trước khi cho start
        [SerializeField] private float minStartSpacingBetweenBlocks = 1.2f;

        // Có vẽ tia ray debug trong Scene view hay không
        [SerializeField] private bool drawDebugCast = true;

        // Tập chứa toàn bộ block đang active trong scene
        public static readonly HashSet<BlockSplineRunner> AllRunners = new();

        // Số block hiện đang chạy
        public static int RunningCount { get; private set; }

        // Event bắn ra khi RunningCount thay đổi
        public static event Action<int> OnRunningCountChanged;

        // Spline mà block sẽ chạy theo
        private SplineContainer splineContainer;

        // Danh sách slot để block quay về khi xong
        private SlotReturn[] slotReturns;

        // Transform của map, dùng để xác định hướng bắn vào phía trong
        private Transform mapTransform;

        // Component animate chạy spline
        private SplineAnimate splineAnimate;

        // Cache collider của block
        private Collider cachedCollider;

        // Cache renderer của block
        private MeshRenderer cachedRenderer;

        // Dùng để lấy / set màu mà không cần clone material
        private MaterialPropertyBlock _mpb;

        // Slot hiện tại block đang giữ
        private SlotReturn currentSlotReturn;

        // Block có đang chạy trên spline hay không
        private bool isRunning;

        // Block có đang trong animation trở về slot hay không
        private bool isReturningToSlot;

        // Lưu ID các cell đã bị xóa, để không xử lý lại
        private readonly HashSet<int> deletedInstanceIds = new();

        // Lưu ID các cell đang có projectile bay tới
        private readonly HashSet<int> pendingInstanceIds = new();

        // ID của cell gần nhất mà ray đang chạm
        private int lastHitCellId = -1;

        // ID của cell vừa bắn gần nhất
        private int lastShotCellId = -1;

        // Thời điểm sớm nhất được phép bắn tiếp
        private float nextAllowedShootTime;

        // Camera chính dùng để raycast khi click vào block
        private static Camera cam;

        // Số đạn còn lại
        private int remainingShots;

        // Nếu true thì khi hết đạn sẽ chờ projectile bay xong rồi mới destroy
        private bool shouldDestroyWhenProjectilesFinish;

        // Nếu true thì block đang trong quá trình destroy vì hết đạn
        private bool isDestroyingOutOfShots;

        // Pool chứa các projectile
        private PrefabPool<ColorProjectile> projectilePool;

        // Pool chứa các block
        private PrefabPool<BlockSplineRunner> blockPool;

        // Component projectile lấy từ prefab
        private ColorProjectile projectilePrefabComponent;

        // Scale ban đầu của block để reset lại khi spawn từ pool
        private Vector3 initialLocalScale;
        
        [Inject]
        public void Construct(
            SplineContainer SplineContainer,  
            RenderMap RenderMap,             
            SlotReturn[] SlotReturns)        
        {
            splineContainer = SplineContainer;
            mapTransform = RenderMap != null ? RenderMap.transform : null;
            slotReturns = SlotReturns;
        }

        // Gán pool của block để sau này trả block về pool
        public void SetPool(PrefabPool<BlockSplineRunner> prefabPool)
        {
            blockPool = prefabPool;
        }

        // Hàm được gọi khi object được lấy ra từ pool
        public void OnSpawned()
        {
            gameObject.SetActive(true);
            transform.localScale = initialLocalScale;
            isDestroyingOutOfShots = false;
            isReturningToSlot = false;
            shouldDestroyWhenProjectilesFinish = false;
            ReleaseCurrentSlot();
        }

        // Hàm được gọi khi object bị trả về pool
        public void OnDespawned()
        {
            StopRunningState();
            ReleaseCurrentSlot();
            deletedInstanceIds.Clear();
            pendingInstanceIds.Clear();
            lastHitCellId = -1;
            lastShotCellId = -1;
            nextAllowedShootTime = 0f;
            isDestroyingOutOfShots = false;
            isReturningToSlot = false;
            shouldDestroyWhenProjectilesFinish = false;
            gameObject.SetActive(false);
        }

        // Awake chạy khi object được tạo lần đầu
        private void Awake()
        {
            splineAnimate = GetComponent<SplineAnimate>();
            cachedCollider = GetComponent<Collider>();
            cachedRenderer = GetComponent<MeshRenderer>();
            
            initialLocalScale = transform.localScale;
            projectilePrefabComponent = hitPrefab != null ? hitPrefab.GetComponent<ColorProjectile>() : null;

            if (projectilePrefabComponent != null)
                projectilePool = new PrefabPool<ColorProjectile>(projectilePrefabComponent, null);
            if (splineAnimate != null)
                splineAnimate.PlayOnAwake = false;
            CacheMainCamera();
            UpdateRemainingShotsText();
        }

        private void OnEnable()
        {
            AllRunners.Add(this);
        }

        private void OnDisable()
        {
            StopRunningState();
            AllRunners.Remove(this);
        }

        private void Update()
        {
            if (Input.GetMouseButtonDown(0)) HandleClick();
            if (!isRunning) return;
            if (mapTransform == null) return;

            // Trong lúc block đang chạy thì raycast để tìm cell hợp lệ và bắn
            DetectAndShootNearestCellByRaycast();

            // Đồng thời kiểm tra đã chạy hết spline chưa
            CheckSplineFinished();
        }

        // Xử lý click chuột vào block
        private void HandleClick()
        {
            CacheMainCamera();
            if (cam == null) return;
            Ray ray = cam.ScreenPointToRay(Input.mousePosition);
            if (!Physics.Raycast(ray, out RaycastHit hit)) return;
            if (!IsHitThisBlock(hit.collider)) return;
            StartRun();
        }

        // Hàm public để bắt đầu chạy block
        public bool StartRun()
        {
            if (!CanStartRun()) return false;
            ReleaseCurrentSlot();
            deletedInstanceIds.Clear();
            pendingInstanceIds.Clear();
            lastHitCellId = -1;
            lastShotCellId = -1;
            nextAllowedShootTime = 0f;
            splineAnimate.Container = splineContainer;
            splineAnimate.Restart(true);
            splineAnimate.Play();
            StartRunningState();
            return true;
        }

        // Kiểm tra toàn bộ điều kiện để block được phép chạy
        private bool CanStartRun()
        {
            if (splineAnimate == null) return false;
            if (splineContainer == null) return false;
            if (mapTransform == null) return false;
            if (isRunning) return false;
            if (RunningCount >= maxConcurrentRunningBlocks) return false;
            if (!HasEnoughSpacingFromRunningBlocks()) return false;
            return true;
        }

        // Kiểm tra khoảng cách với các block đang chạy khác
        private bool HasEnoughSpacingFromRunningBlocks()
        {
            Vector3 myPosition = transform.position;
            float minDistanceSqr = minStartSpacingBetweenBlocks * minStartSpacingBetweenBlocks;
            foreach (BlockSplineRunner other in AllRunners)
            {
                if (other == null) continue;
                if (other == this) continue;
                if (!other.isRunning) continue;
                if (!other.gameObject.activeInHierarchy) continue;
                float sqrDistance = (other.transform.position - myPosition).sqrMagnitude;
                if (sqrDistance < minDistanceSqr) return false;
            }
            return true;
        }

        // Gán số đạn ban đầu cho block
        public void InitializeShotLimit(int shotCount)
        {
            remainingShots = Mathf.Max(0, shotCount);
            shouldDestroyWhenProjectilesFinish = remainingShots == 0;
            UpdateRemainingShotsText();
        }

        // Cache camera chính
        private void CacheMainCamera()
        {
            if (cam == null || !cam.isActiveAndEnabled) cam = Camera.main;
        }

        // Kiểm tra collider hit có phải là block này không
        private bool IsHitThisBlock(Collider hitCollider)
        { 
            if (hitCollider == null) return false;
            if (hitCollider.gameObject == gameObject) return true;
            if (cachedCollider != null && hitCollider == cachedCollider) return true;
            return hitCollider.transform.IsChildOf(transform);
        }

        // Raycast vào map để tìm cell gần nhất hợp lệ rồi thử bắn
        private void DetectAndShootNearestCellByRaycast()
        {
            Vector3 castDirection = GetInwardDirection();
            if (castDirection == Vector3.zero) return;
            Vector3 origin = transform.position - castDirection * originBackOffset;
            // if (drawDebugCast) Debug.DrawRay(origin, castDirection * castDistance, Color.green);
            RaycastHit[] hits = Physics.RaycastAll(origin, castDirection, castDistance, cellLayerMask);

            if (hits == null || hits.Length == 0)
            {
                lastHitCellId = -1;
                return;
            }
            Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

            // Lấy cell hợp lệ gần nhất từ mảng hit đã sort
            ColorCell nearestCell = GetNearestValidCellFromRay(hits);

            if (nearestCell == null)
            {
                lastHitCellId = -1;
                return;
            }

            int currentCellId = nearestCell.gameObject.GetInstanceID();
            if (currentCellId == lastHitCellId) return;
            lastHitCellId = currentCellId;
            if (Time.time < nextAllowedShootTime) return;
            ShootCellIfValid(nearestCell);
        }

        // Từ mảng hit đã sort, tìm cell gần nhất hợp lệ
        private ColorCell GetNearestValidCellFromRay(RaycastHit[] hits)
        {
            for (int i = 0; i < hits.Length; i++)
            {
                Collider hitCollider = hits[i].collider;
                if (hitCollider == null) continue;
                ColorCell cell = hitCollider.GetComponent<ColorCell>();
                if (cell == null)
                    cell = hitCollider.GetComponentInParent<ColorCell>();

                if (cell == null) continue;
                int id = cell.gameObject.GetInstanceID();
                if (deletedInstanceIds.Contains(id)) continue;
                if (pendingInstanceIds.Contains(id)) continue;
                return cell;
            }
            return null;
        }

        // Xác định hướng bắn vào phía trong map dựa trên vị trí block
        private Vector3 GetInwardDirection()
        {
            Vector3 localPos = mapTransform.InverseTransformPoint(transform.position);

            float absX = Mathf.Abs(localPos.x);
            float absY = Mathf.Abs(localPos.y);
            if (absX >= absY)
                return localPos.x > 0f ? -mapTransform.right : mapTransform.right;
            return localPos.y > 0f ? -mapTransform.up : mapTransform.up;
        }

        // Bắn cell nếu cell hợp lệ
        private void ShootCellIfValid(ColorCell cell)
        {
            if (cell == null) return;
            if (remainingShots <= 0) return;
            int id = cell.gameObject.GetInstanceID();
            if (deletedInstanceIds.Contains(id)) return;
            if (pendingInstanceIds.Contains(id)) return;
            if (id == lastShotCellId) return;
            Color myColor = GetMyColor();
            if (!IsSameColor(myColor, cell.CellColor)) return;
            if (hitPrefab == null) return;
            if (projectilePool == null)
            {
                projectilePrefabComponent = hitPrefab.GetComponent<ColorProjectile>();
                if (projectilePrefabComponent == null) return;
                projectilePool = new PrefabPool<ColorProjectile>(projectilePrefabComponent, null);
            }
            Vector3 spawnPos = projectileSpawnPoint != null ? projectileSpawnPoint.position : transform.position;
            ColorProjectile projectile = projectilePool.Get();
            projectile.transform.SetPositionAndRotation(spawnPos, Quaternion.identity);
            projectile.Initialize(cell, myColor, colorTolerance, projectileSpeed, this);
            pendingInstanceIds.Add(id);
            lastShotCellId = id;
            nextAllowedShootTime = Time.time + shootCooldown;
            remainingShots--;
            if (remainingShots <= 0)
                shouldDestroyWhenProjectilesFinish = true;
            UpdateRemainingShotsText();
        }

        // Projectile gọi ngược về đây khi hoàn thành hành trình
        public void NotifyProjectileFinished(ColorCell cell, bool deleteCell)
        {
            if (cell == null) return;
            int id = cell.gameObject.GetInstanceID();
            pendingInstanceIds.Remove(id);
            if (deleteCell)
            {
                deletedInstanceIds.Add(id);
                if (cell.gameObject != null)
                    cell.PlayDestroyAnimation();
            }
            DestroyIfOutOfShots();
        }

        // Kiểm tra block đã đi hết spline chưa
        private void CheckSplineFinished()
        {
            if (splineAnimate == null)
            {
                StopRunningState();
                return;
            }
            if (splineAnimate.NormalizedTime < 1f) return;

            if (ShouldLoopInsteadOfReturnToSlot())
            {
                RestartSplineLoop();
                return;
            }
            StopRunningState();
            MoveToSlotReturn();
        }

        // Quyết định block nên loop lại spline hay quay về slot
        private bool ShouldLoopInsteadOfReturnToSlot()
        {
            return AllRunners.Count <= maxConcurrentRunningBlocks;
        }

        // Restart spline từ đầu
        private void RestartSplineLoop()
        {
            if (splineAnimate == null) return;
            ReleaseCurrentSlot();

            splineAnimate.Restart(true);
            splineAnimate.Play();
        }

        // Đánh dấu block bắt đầu chạy
        private void StartRunningState()
        {
            if (isRunning) return;
            isRunning = true;
            RunningCount++;
            OnRunningCountChanged?.Invoke(RunningCount);
        }

        // Đánh dấu block dừng chạy
        private void StopRunningState()
        {
            if (!isRunning) return;
            isRunning = false;
            RunningCount = Mathf.Max(0, RunningCount - 1);
            OnRunningCountChanged?.Invoke(RunningCount);
        }

        // Tìm slot trống và cho block quay về đó
        private void MoveToSlotReturn()
        {
            if (slotReturns == null || slotReturns.Length == 0) return;

            for (int i = 0; i < slotReturns.Length; i++)
            {
                SlotReturn slot = slotReturns[i];
                if (slot == null) continue;
                if (slot.IsOccupied) continue;
                slot.SetOccupied(true);

                currentSlotReturn = slot;
                AnimateMoveToSlotReturn(slot);
                return;
            }
        }

        // Thả slot hiện tại nếu đang giữ
        private void ReleaseCurrentSlot()
        {
            if (currentSlotReturn == null) return;
            currentSlotReturn.SetOccupied(false);
            currentSlotReturn = null;
        }

        // Animate block nhảy về slot
        private void AnimateMoveToSlotReturn(SlotReturn slotReturn)
        {
            if (slotReturn == null) return;
            if (isReturningToSlot) return;
            isReturningToSlot = true;
            Vector3 targetLocalPosition = GetTargetLocalPosition(slotReturn.transform.position);
            float baseLocalZ = targetLocalPosition.z;
            float duration = Mathf.Max(0.01f, returnJumpDuration);

            Sequence jumpSequence = Sequence.Create()
                .Chain(Tween.LocalPositionZ(transform, baseLocalZ + returnJumpHeight, duration * 0.5f, Ease.OutQuad))
                .Chain(Tween.LocalPositionZ(transform, baseLocalZ, duration * 0.5f, Ease.InQuad));

            Sequence.Create()
                .Group(Tween.LocalPosition(transform, targetLocalPosition, duration, Ease.InOutSine))
                .Group(jumpSequence)
                .OnComplete(this, target =>
                {
                    target.transform.rotation = slotReturn.transform.rotation;
                    target.isReturningToSlot = false;
                });
        }

        // Đổi world position sang local position theo parent của block
        private Vector3 GetTargetLocalPosition(Vector3 worldPosition)
        {
            // Nếu block không có parent thì trả world position luôn
            if (transform.parent == null)
                return worldPosition;
            // Nếu có parent thì đổi về local space
            return transform.parent.InverseTransformPoint(worldPosition);
        }

        // Nếu block hết đạn và không còn projectile pending thì destroy block
        private void DestroyIfOutOfShots()
        { 
            if (!shouldDestroyWhenProjectilesFinish) return;
            if (pendingInstanceIds.Count > 0) return;
            if (isDestroyingOutOfShots) return;
            isDestroyingOutOfShots = true;

            StopRunningState();
            ReleaseCurrentSlot();

            Tween.Scale(transform, Vector3.zero, Mathf.Max(0.01f, outOfShotsDestroyDuration), Ease.OutQuad)
                .OnComplete(this, target =>
                {
                    if (target.blockPool != null)
                        target.blockPool.Release(target);
                    else
                        target.gameObject.SetActive(false);
                });
        }

        // Trả projectile về pool
        public void ReleaseProjectile(ColorProjectile projectile)
        {
            // Nếu có projectile pool thì release về pool
            if (projectilePool != null)
                projectilePool.Release(projectile);

            // Nếu không thì chỉ tắt object
            else if (projectile != null)
                projectile.gameObject.SetActive(false);
        }

        // Lấy màu hiện tại của block
        public Color GetMyColor()
        {
            if (cachedRenderer == null)
                return Color.white;

            if (_mpb == null)
                _mpb = new MaterialPropertyBlock();

            cachedRenderer.GetPropertyBlock(_mpb);

            if (cachedRenderer.sharedMaterial != null && cachedRenderer.sharedMaterial.HasProperty("_BaseColor"))
            {
                Color c = _mpb.GetColor("_BaseColor");
                if (c != default) return c;
                return cachedRenderer.material.GetColor("_BaseColor");
            }
            if (cachedRenderer.sharedMaterial != null && cachedRenderer.sharedMaterial.HasProperty("_Color"))
            {
                Color c = _mpb.GetColor("_Color");
                if (c != default) return c;
                return cachedRenderer.material.GetColor("_Color");
            }
            return Color.white;
        }

        // Cập nhật text hiển thị số đạn
        private void UpdateRemainingShotsText()
        { if (remainingShotsText == null) return;
            remainingShotsText.text = remainingShots.ToString();
        }

        // So màu theo khoảng cách giữa 2 vector RGB
        private bool IsSameColor(Color a, Color b)
        {
            // Chuyển màu a và b thành vector RGB rồi tính khoảng cách
            return Vector3.Distance(
                new Vector3(a.r, a.g, a.b),
                new Vector3(b.r, b.g, b.b)
            ) <= colorTolerance;
        }
    }
}