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
        // Vị trí bắn projectile ra ngoài
        [SerializeField] private Transform projectileSpawnPoint;

        // LayerMask để raycast chỉ trúng đúng layer mong muốn
        [SerializeField] private LayerMask cellLayerMask = ~0;

        // Prefab projectile dùng để bắn vào cell
        [SerializeField] private GameObject hitPrefab;

        // Text hiển thị số đạn còn lại
        [SerializeField] private TMP_Text remainingShotsText;

        // Độ lệch màu cho phép khi so block với cell
        [SerializeField] private float colorTolerance = 0.05f;

        // Chiều dài tia raycast từ block bắn vào map
        [SerializeField] private float castDistance = 8f;

        // Dời điểm bắt đầu cast lùi nhẹ về phía sau để ray ổn định hơn
        [SerializeField] private float originBackOffset = 0.15f;

        // Tốc độ bay của projectile
        [SerializeField] private float projectileSpeed = 12f;

        // Khoảng nghỉ giữa 2 phát bắn
        [SerializeField] private float shootCooldown = 0.03f;

        // Thời gian scale nhỏ lại khi block hết đạn
        [SerializeField] private float outOfShotsDestroyDuration = 0.15f;

        // Thời gian block nhảy về slot
        [SerializeField] private float returnJumpDuration = 0.35f;

        // Độ cao cú nhảy khi về slot
        [SerializeField] private float returnJumpHeight = 0.6f;

        // Số block tối đa được chạy cùng lúc
        [SerializeField] private int maxConcurrentRunningBlocks = 5;

        // Khoảng cách tối thiểu giữa block này và block khác trước khi cho bắt đầu chạy
        [SerializeField] private float minStartSpacingBetweenBlocks = 1.2f;

        // Có vẽ ray debug trong Scene view hay không
        [SerializeField] private bool drawDebugCast = true;

        // Tập hợp chứa toàn bộ BlockSplineRunner đang active trong scene
        public static readonly HashSet<BlockSplineRunner> AllRunners = new();

        // Số block hiện đang ở trạng thái chạy
        public static int RunningCount { get; private set; }

        // Event báo ra ngoài khi RunningCount thay đổi
        public static event Action<int> OnRunningCountChanged;

        // Spline mà block sẽ chạy theo
        private SplineContainer splineContainer;

        // Danh sách slot để block quay về khi chạy xong
        private SlotReturn[] slotReturns;

        // Transform của map, dùng để xác định hướng bắn vào trong
        private Transform mapTransform;

        // Component animate chạy theo spline
        private SplineAnimate splineAnimate;

        // Cache collider của block để tránh GetComponent nhiều lần
        private Collider cachedCollider;

        // Cache renderer để đọc màu block
        private MeshRenderer cachedRenderer;

        // MaterialPropertyBlock để làm việc với màu mà không phải clone material
        private MaterialPropertyBlock _mpb;

        // Slot hiện tại block đang chiếm
        private SlotReturn currentSlotReturn;

        // Cờ cho biết block có đang chạy trên spline hay không
        private bool isRunning = false;

        // Cờ cho biết block có đang trong quá trình nhảy về slot hay không
        private bool isReturningToSlot = false;

        // Lưu ID của các cell đã bị xóa
        private readonly HashSet<int> deletedInstanceIds = new();

        // Lưu ID của các cell đang có projectile bay tới
        private readonly HashSet<int> pendingInstanceIds = new();

        // Lưu ID cell gần nhất raycast đang chạm tới
        private int lastHitCellId = -1;

        // Lưu ID cell gần nhất vừa bắn
        private int lastShotCellId = -1;

        // Thời điểm sớm nhất được phép bắn tiếp
        private float nextAllowedShootTime = 0f;

        // Camera chính dùng để bắn ray khi click chuột
        private static Camera cam;

        // Số block đang chạy toàn cục
        private static int runningBlockCount = 0;

        // Tập hợp các block đang active
        private static readonly HashSet<BlockSplineRunner> activeBlocks = new();

        // Số đạn còn lại
        private int remainingShots;

        // Nếu true, sau khi projectile bay xong thì block sẽ biến mất
        private bool shouldDestroyWhenProjectilesFinish;

        // Nếu true, block đang trong quá trình destroy do hết đạn
        private bool isDestroyingOutOfShots;

        // Pool của projectile
        private PrefabPool<ColorProjectile> projectilePool;

        // Pool của chính block
        private PrefabPool<BlockSplineRunner> blockPool;

        // Component projectile lấy từ prefab
        private ColorProjectile projectilePrefabComponent;

        // Scale ban đầu của block để reset khi lấy lại từ pool
        private Vector3 initialLocalScale;

        [Inject]
        public void Construct(SplineContainer SplineContainer, RenderMap RenderMap, SlotReturn[] SlotReturns)  
        {
            splineContainer = SplineContainer;
            mapTransform = RenderMap != null ? RenderMap.transform : null; 
            slotReturns = SlotReturns;
        }

        public void SetPool(PrefabPool<BlockSplineRunner> prefabPool)
        {
            blockPool = prefabPool;
        }

        // Hàm gọi khi object được lấy ra từ pool
        public void OnSpawned()
        {
            gameObject.SetActive(true);
            transform.localScale = initialLocalScale;
            isDestroyingOutOfShots = false;
            isReturningToSlot = false;
            shouldDestroyWhenProjectilesFinish = false;
            ReleaseCurrentSlot();
        }

        // Hàm gọi khi object bị trả về pool
        public void OnDespawned()
        {
            StopRunningState();
            ReleaseCurrentSlot();
            deletedInstanceIds.Clear();
            pendingInstanceIds.Clear();
            lastHitCellId = -1;
            lastShotCellId = -1;
            isDestroyingOutOfShots = false;
            isReturningToSlot = false;
            shouldDestroyWhenProjectilesFinish = false;
            gameObject.SetActive(false);
        }

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
            activeBlocks.Add(this);
        }

        private void OnDisable()
        {
            StopRunningState();
            activeBlocks.Remove(this);
            AllRunners.Remove(this);
        }

        private void Update()
        {
            if (Input.GetMouseButtonDown(0))
                HandleClick();

            if (isRunning)
            {
                if (mapTransform == null) return;
                DetectAndShootNearestCellByRaycast();
                CheckSplineFinished();
            }
        }

        // Xử lý click chuột vào block
        private void HandleClick()
        {
            CacheMainCamera();
            if (cam == null) return;
            Ray ray = cam.ScreenPointToRay(Input.mousePosition);

            if (Physics.Raycast(ray, out RaycastHit hit))
            {
                if (IsHitThisBlock(hit.collider))
                {
                    if (isRunning) return;
                    if (runningBlockCount >= maxConcurrentRunningBlocks) return;
                    TryRun();
                }
            }
        }

        // Hàm thử cho block bắt đầu chạy
        public bool TryRun()
        {
            if (!CanStartRun())
                return false;
            RunInternal();
            return true;
        }
        
        // Check toàn bộ điều kiện trước khi cho block chạy
        private bool CanStartRun()
        {
            if (splineAnimate == null) return false;
            if (splineContainer == null) return false;
            if (mapTransform == null) return false;
            if (isRunning) return false;
            if (runningBlockCount >= maxConcurrentRunningBlocks) return false;
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

        // Hàm chạy thật sự sau khi đã check xong điều kiện
        private void RunInternal()
        {
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
        }

        // Gán số đạn ban đầu
        public void InitializeShotLimit(int shotCount)
        {
            remainingShots = Mathf.Max(0, shotCount);
            // Nếu ngay từ đầu số đạn là 0 thì đánh dấu sẽ destroy khi projectile xong
            shouldDestroyWhenProjectilesFinish = remainingShots == 0;
            UpdateRemainingShotsText();
        }

        // Cache camera chính
        private void CacheMainCamera()
        {
            if (cam == null || !cam.isActiveAndEnabled)
                cam = Camera.main;
        }

        // Kiểm tra collider vừa hit có phải là block này không
        private bool IsHitThisBlock(Collider hitCollider)
        {
            if (hitCollider == null) return false;
            if (hitCollider.gameObject == gameObject) return true;
            if (cachedCollider != null && hitCollider == cachedCollider) return true;
            return hitCollider.transform.IsChildOf(transform);
        }

        // Raycast vào map để tìm cell gần nhất và thử bắn
        private void DetectAndShootNearestCellByRaycast()
        {
            if (mapTransform == null) return;
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
            TryShootCell(nearestCell);
        }

        // Từ mảng raycast hit, tìm ra cell hợp lệ gần nhất
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

        // Xác định hướng block sẽ raycast vào trong map
        private Vector3 GetInwardDirection()
        {
            // Chuyển position của block sang local space của map
            Vector3 localPos = mapTransform.InverseTransformPoint(transform.position);

            // Lấy trị tuyệt đối để biết block gần cạnh nào hơn
            float absX = Mathf.Abs(localPos.x);
            float absY = Mathf.Abs(localPos.y);

            // Nếu gần cạnh trái / phải hơn
            if (absX >= absY)
            {
                // Nếu block nằm bên phải map => bắn sang trái
                if (localPos.x > 0f) return -mapTransform.right;

                // Nếu block nằm bên trái map => bắn sang phải
                return mapTransform.right;
            }

            // Nếu gần cạnh trên / dưới hơn
            // Nếu block ở phía trên => bắn xuống
            if (localPos.y > 0f) return -mapTransform.up;

            // Nếu block ở phía dưới => bắn lên
            return mapTransform.up;
        }

        // Thử bắn một cell
        private void TryShootCell(ColorCell cell)
        {
            // Không có cell thì không bắn
            if (cell == null) return;

            // Hết đạn thì không bắn
            if (remainingShots <= 0) return;

            // Lấy ID cell
            int id = cell.gameObject.GetInstanceID();

            // Nếu cell đã bị xóa thì không bắn
            if (deletedInstanceIds.Contains(id)) return;

            // Nếu cell đang có projectile khác bay tới thì không bắn
            if (pendingInstanceIds.Contains(id)) return;

            // Nếu đây là cell vừa mới bắn ở lần trước thì không bắn lặp ngay
            if (id == lastShotCellId) return;

            // Lấy màu hiện tại của block
            Color myColor = GetMyColor();

            // Lấy màu của cell
            Color cellColor = cell.CellColor;

            // Nếu màu không giống nhau thì không bắn
            if (!IsSameColor(myColor, cellColor)) return;

            // Không có hitPrefab thì không bắn được
            if (hitPrefab == null) return;

            // Nếu chưa có projectile pool thì tạo
            if (projectilePool == null)
            {
                projectilePrefabComponent = hitPrefab.GetComponent<ColorProjectile>();
                if (projectilePrefabComponent == null) return;

                projectilePool = new PrefabPool<ColorProjectile>(projectilePrefabComponent, null);
            }

            // Lấy vị trí bắn, ưu tiên projectileSpawnPoint
            Vector3 spawnPos = projectileSpawnPoint != null ? projectileSpawnPoint.position : transform.position;

            // Lấy projectile từ pool
            ColorProjectile projectile = projectilePool.Get();

            // Đặt vị trí và rotation ban đầu cho projectile
            projectile.transform.SetPositionAndRotation(spawnPos, Quaternion.identity);

            // Truyền dữ liệu vào projectile để nó bay tới cell
            projectile.Initialize(cell, myColor, colorTolerance, projectileSpeed, this);

            // Đánh dấu cell này đang có projectile bay tới
            pendingInstanceIds.Add(id);

            // Lưu lại cell vừa bắn
            lastShotCellId = id;

            // Đặt cooldown cho phát bắn kế tiếp
            nextAllowedShootTime = Time.time + shootCooldown;

            // Trừ 1 đạn
            remainingShots--;

            // Nếu vừa hết đạn thì đánh dấu sẽ destroy sau khi projectile bay xong
            if (remainingShots <= 0)
                shouldDestroyWhenProjectilesFinish = true;

            // Cập nhật text UI
            UpdateRemainingShotsText();
        }

        // Projectile gọi ngược về block khi projectile kết thúc
        public void NotifyProjectileFinished(ColorCell cell, bool deleteCell)
        {
            // Không có cell thì thôi
            if (cell == null) return;

            // Lấy ID cell
            int id = cell.gameObject.GetInstanceID();

            // Bỏ cell này khỏi danh sách pending
            pendingInstanceIds.Remove(id);

            // Nếu projectile báo rằng cell nên bị xóa
            if (deleteCell)
            {
                // Nếu cell chưa nằm trong danh sách deleted thì thêm vào
                if (!deletedInstanceIds.Contains(id))
                    deletedInstanceIds.Add(id);

                // Cho cell chạy animation phá hủy
                if (cell.gameObject != null)
                    cell.PlayDestroyAnimation();
            }

            // Kiểm tra xem block có cần biến mất không
            TryDestroyIfOutOfShots();
        }

        // Kiểm tra block đã đi hết spline chưa
        private void CheckSplineFinished()
        {
            // Nếu mất SplineAnimate thì dừng ngay
            if (splineAnimate == null)
            {
                StopRunningState();
                return;
            }

            // Nếu đã chạy tới cuối spline
            if (splineAnimate.NormalizedTime >= 1f)
            {
                // Nếu còn điều kiện loop tiếp thì restart spline
                if (ShouldLoopInsteadOfReturnToSlot())
                {
                    RestartSplineLoop();
                    return;
                }

                // Nếu không loop nữa thì dừng chạy và về slot
                StopRunningState();
                MoveToSlotReturn();
            }
        }

        // Quyết định có nên loop spline tiếp hay không
        private bool ShouldLoopInsteadOfReturnToSlot()
        {
            // Nếu số block active <= giới hạn maxConcurrent thì cho loop tiếp
            return GetActiveBlockCount() <= maxConcurrentRunningBlocks;
        }

        // Chạy lại spline từ đầu
        private void RestartSplineLoop()
        {
            // Nếu mất SplineAnimate thì thôi
            if (splineAnimate == null) return;

            // Thả slot hiện tại trước
            ReleaseCurrentSlot();

            // Restart và play lại spline
            splineAnimate.Restart(true);
            splineAnimate.Play();
        }

        // Đếm số block đang active
        private int GetActiveBlockCount()
        {
            // Xóa các block null hoặc không còn active khỏi set
            activeBlocks.RemoveWhere(block => block == null || !block.gameObject.activeInHierarchy);

            // Trả về số lượng còn lại
            return activeBlocks.Count;
        }

        // Bắt đầu trạng thái đang chạy
        private void StartRunningState()
        {
            // Nếu đang chạy rồi thì không tăng count lại
            if (isRunning) return;

            // Đánh dấu block đang chạy
            isRunning = true;

            // Tăng count nội bộ
            runningBlockCount++;

            // Tăng count public
            RunningCount++;

            // Bắn event cho UI / manager biết count đã đổi
            OnRunningCountChanged?.Invoke(RunningCount);
        }

        // Dừng trạng thái đang chạy
        private void StopRunningState()
        {
            // Nếu block vốn không chạy thì thôi
            if (!isRunning) return;

            // Đánh dấu block không còn chạy
            isRunning = false;

            // Giảm count nội bộ nhưng không cho âm
            runningBlockCount = Mathf.Max(0, runningBlockCount - 1);

            // Giảm count public nhưng không cho âm
            RunningCount = Mathf.Max(0, RunningCount - 1);

            // Bắn event cập nhật UI / manager
            OnRunningCountChanged?.Invoke(RunningCount);
        }

        // Cho block di chuyển về slot trống đầu tiên
        private void MoveToSlotReturn()
        {
            // Không có slot nào thì thôi
            if (slotReturns == null || slotReturns.Length == 0) return;

            // Duyệt toàn bộ slot
            for (int i = 0; i < slotReturns.Length; i++)
            {
                // Nếu slot null thì bỏ qua
                if (slotReturns[i] == null) continue;

                // Nếu slot đã bị chiếm thì bỏ qua
                if (slotReturns[i].IsOccupied) continue;

                // Đánh dấu slot này đã bị chiếm
                slotReturns[i].SetOccupied(true);

                // Lưu slot hiện tại
                currentSlotReturn = slotReturns[i];

                // Animate block về slot này
                AnimateMoveToSlotReturn(slotReturns[i]);
                return;
            }
        }

        // Thả slot hiện tại nếu đang giữ
        private void ReleaseCurrentSlot()
        {
            // Nếu không giữ slot nào thì thôi
            if (currentSlotReturn == null) return;

            // Đánh dấu slot là trống
            currentSlotReturn.SetOccupied(false);

            // Xóa ref slot hiện tại
            currentSlotReturn = null;
        }

        // Animate block nhảy về slot
        private void AnimateMoveToSlotReturn(SlotReturn slotReturn)
        {
            // Không có slot thì thôi
            if (slotReturn == null) return;

            // Nếu đang return rồi thì không chạy tiếp
            if (isReturningToSlot) return;

            // Đánh dấu đang return
            isReturningToSlot = true;

            // Đổi world position của slot về local position theo parent của block
            Vector3 targetLocalPosition = GetTargetLocalPosition(slotReturn.transform.position);

            // Lưu local Z đích
            float baseLocalZ = targetLocalPosition.z;

            // Đảm bảo duration > 0
            float duration = Mathf.Max(0.01f, returnJumpDuration);

            // Tạo sequence nhảy: đi lên rồi hạ xuống
            Sequence jumpSequence = Sequence.Create()
                .Chain(Tween.LocalPositionZ(transform, baseLocalZ + returnJumpHeight, duration * 0.5f, Ease.OutQuad))
                .Chain(Tween.LocalPositionZ(transform, baseLocalZ, duration * 0.5f, Ease.InQuad));

            // Tạo sequence vừa di chuyển ngang vừa nhảy
            Sequence.Create()
                .Group(Tween.LocalPosition(transform, targetLocalPosition, duration, Ease.InOutSine))
                .Group(jumpSequence)
                .OnComplete(this, target =>
                {
                    // Khi xong thì xoay block theo rotation của slot
                    target.transform.rotation = slotReturn.transform.rotation;

                    // Đánh dấu đã return xong
                    target.isReturningToSlot = false;
                });
        }

        // Đổi world position sang local position theo parent của block
        private Vector3 GetTargetLocalPosition(Vector3 worldPosition)
        {
            // Nếu block không có parent thì dùng world position luôn
            if (transform.parent == null)
                return worldPosition;

            // Nếu có parent thì đổi về local space
            return transform.parent.InverseTransformPoint(worldPosition);
        }

        // Nếu hết đạn và không còn projectile pending thì destroy block
        private void TryDestroyIfOutOfShots()
        {
            // Nếu chưa tới trạng thái cần destroy thì thôi
            if (!shouldDestroyWhenProjectilesFinish) return;

            // Nếu vẫn còn projectile đang bay thì chờ
            if (pendingInstanceIds.Count > 0) return;

            // Nếu đang destroy rồi thì không chạy lại
            if (isDestroyingOutOfShots) return;

            // Đánh dấu đang destroy
            isDestroyingOutOfShots = true;

            // Dừng trạng thái chạy
            StopRunningState();

            // Thả slot hiện tại
            ReleaseCurrentSlot();

            // Scale block về 0
            Tween.Scale(transform, Vector3.zero, Mathf.Max(0.01f, outOfShotsDestroyDuration), Ease.OutQuad)
                .OnComplete(this, target =>
                {
                    // Nếu có pool thì trả block về pool
                    if (target.blockPool != null)
                        target.blockPool.Release(target);

                    // Nếu không có pool thì chỉ tắt object
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

            // Nếu không thì tắt object
            else if (projectile != null)
                projectile.gameObject.SetActive(false);
        }

        // Lấy màu hiện tại của block
        public Color GetMyColor()
        {
            // Nếu không có renderer thì mặc định trắng
            if (cachedRenderer == null)
                return Color.white;

            // Nếu chưa có MPB thì tạo mới
            if (_mpb == null)
                _mpb = new MaterialPropertyBlock();

            // Lấy property block hiện tại từ renderer
            cachedRenderer.GetPropertyBlock(_mpb);

            // Nếu material có _BaseColor
            if (cachedRenderer.sharedMaterial != null && cachedRenderer.sharedMaterial.HasProperty("_BaseColor"))
            {
                // Lấy màu từ property block
                Color c = _mpb.GetColor("_BaseColor");

                // Nếu property block có màu hợp lệ thì trả về nó
                if (c != default)
                    return c;

                // Nếu không thì fallback lấy màu trực tiếp từ material
                return cachedRenderer.material.GetColor("_BaseColor");
            }

            // Nếu material có _Color
            if (cachedRenderer.sharedMaterial != null && cachedRenderer.sharedMaterial.HasProperty("_Color"))
            {
                // Lấy màu từ property block
                Color c = _mpb.GetColor("_Color");

                // Nếu property block có màu hợp lệ thì dùng
                if (c != default)
                    return c;

                // Nếu không thì fallback từ material
                return cachedRenderer.material.GetColor("_Color");
            }

            // Nếu không đọc được màu thì trả trắng
            return Color.white;
        }

        // Cập nhật text số đạn còn lại
        private void UpdateRemainingShotsText()
        {
            // Nếu không có text thì thôi
            if (remainingShotsText == null) return;

            // Chuyển số đạn thành string và gán lên text
            remainingShotsText.text = remainingShots.ToString();
        }

        // So màu theo khoảng cách RGB với tolerance
        private bool IsSameColor(Color a, Color b)
        {
            // Tạo vector RGB từ màu a và b rồi so khoảng cách Euclidean
            return Vector3.Distance(
                new Vector3(a.r, a.g, a.b),
                new Vector3(b.r, b.g, b.b)
            ) <= colorTolerance;
        }
    }
}