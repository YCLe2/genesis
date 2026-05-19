using UnityEngine;

public class HardwareTagFollower : MonoBehaviour
{
    [Header("📡 실시간 상태 (디버깅용)")]
    public Vector3 targetPosition; // 칼만 필터가 적용된 최종 목표 좌표

    [Header("🏃 이동 및 회전 보간 설정")]
    [Tooltip("목표 위치까지 도달하는 데 걸리는 대략적인 시간(초). 작을수록 빠릅니다. (추천: 0.1 ~ 0.3)")]
    public float smoothTime = 0.15f; 
    public float rotationSpeed = 15f; 
    
    [Tooltip("이동 거리가 이 값(m)보다 작으면 제자리 떨림으로 간주하고 회전하지 않습니다. (추천: 0.15)")]
    public float rotationDeadzone = 0.15f; 

    [Header("🛡️ 노이즈 필터링 설정 (1 Unit = 1m)")]
    [Tooltip("통신 주기 내에 이 거리(m) 이상을 순간이동하면 전파 노이즈로 간주하고 무시합니다.")]
    public float maxAllowedJump = 1.0f; 

    [Header("📊 칼만 필터(Kalman Filter) 튜닝")]
    [Tooltip("시스템 노이즈 (프로세스가 얼마나 빨리 변하는지). 높으면 센서를 더 잘 따라가고, 낮으면 더 부드러워집니다. (추천: 0.001 ~ 0.01)")]
    public float kalmanQ = 0.005f;
    [Tooltip("측정 노이즈 (센서를 얼마나 신뢰할 것인지). 높으면 센서값을 무시(부드러움 증가), 낮으면 센서값을 신뢰(반응성 증가). (추천: 0.01 ~ 0.1)")]
    public float kalmanR = 0.05f;

    [Header("🧱 세이프 존 (공간 경계) 설정")]
    public float minX = -50f; 
    public float maxX = 50f;  
    public float minZ = -50f; 
    public float maxZ = 50f;  

    private Animator _animator;
    private int _animIDSpeed, _animIDMotionSpeed, _animIDGrounded;

    // SmoothDamp를 위한 속도 참조 변수
    private Vector3 _currentVelocity;

    // 필터링 및 상태 확인 변수
    private Vector3 lastValidPosition;
    private bool isFirstSignal = true;

    // 간이 3D 칼만 필터 인스턴스
    private Vector3KalmanFilter _kalmanFilter;

    void Start()
    {
        _animator = GetComponent<Animator>();
        _animIDSpeed = Animator.StringToHash("Speed");
        _animIDMotionSpeed = Animator.StringToHash("MotionSpeed");
        _animIDGrounded = Animator.StringToHash("Grounded");

        if (_animator != null) _animator.SetBool(_animIDGrounded, true);

        targetPosition = transform.position; 
        lastValidPosition = transform.position;

        // 칼만 필터 초기화
        _kalmanFilter = new Vector3KalmanFilter(kalmanQ, kalmanR);
    }

    void Update()
    {
        // 1. 방향 계산 (고개가 위아래로 꺾이는 현상 방지)
        Vector3 direction = targetPosition - transform.position;
        direction.y = 0; 

        // 2. 부드러운 위치 이동 (SmoothDamp 적용)
        // Lerp와 달리 오버슈트(목표를 지나침)가 없고 훨씬 부드럽게 감속합니다.
        transform.position = Vector3.SmoothDamp(transform.position, targetPosition, ref _currentVelocity, smoothTime);

        // 3. 회전 데드존 적용
        float distanceToTarget = direction.magnitude;
        if (distanceToTarget > rotationDeadzone) 
        {
            Quaternion targetRotation = Quaternion.LookRotation(direction);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, Time.deltaTime * rotationSpeed);
        }

        // 4. 애니메이션 재생 (SmoothDamp의 실제 속도인 _currentVelocity.magnitude 사용)
        if (_animator != null)
        {
            // 목표지점과의 거리 대신 '현재 이동하는 물리적 속도'를 기반으로 애니메이션을 재생하면 발끌림 현상이 줄어듭니다.
            float currentSpeed = _currentVelocity.magnitude;
            float animationSpeed = Mathf.Clamp(currentSpeed, 0f, 6f); 
            
            _animator.SetFloat(_animIDSpeed, animationSpeed);
            _animator.SetFloat(_animIDMotionSpeed, 1f); 
        }
    }

    // 통신 스크립트에서 좌표를 넘겨줄 때 호출하는 함수
    public void UpdateHardwarePosition(Vector3 newTagPosition)
    {
        // [방어 1] 공간 경계 밖의 쓰레기 데이터 무시
        if (newTagPosition.x < minX || newTagPosition.x > maxX || 
            newTagPosition.z < minZ || newTagPosition.z > maxZ)
        {
            return; 
        }

        // [방어 2] 처음 들어온 신호는 무조건 정상값으로 세팅
        if (isFirstSignal)
        {
            lastValidPosition = newTagPosition;
            targetPosition = newTagPosition;
            _kalmanFilter.ResetState(newTagPosition); // 칼만 필터 시작점 동기화
            isFirstSignal = false;
            return;
        }

        // [방어 3] 물리적 한계 초과 방어 (순간이동 차단)
        float jumpDistance = Vector3.Distance(lastValidPosition, newTagPosition);
        if (jumpDistance > maxAllowedJump)
        {
            return; 
        }

        // [방어 4] 칼만 필터(Kalman Filter) 전처리 적용
        // 실시간으로 변하는 Q/R 값을 필터에 업데이트 (인스펙터 튜닝용)
        _kalmanFilter.Q = kalmanQ;
        _kalmanFilter.R = kalmanR;

        Vector3 filteredPosition = _kalmanFilter.Update(newTagPosition);
        
        targetPosition = filteredPosition;
        lastValidPosition = filteredPosition;
    }
}

/// <summary>
/// 각 축(X, Y, Z)에 독립적으로 작동하는 간이 칼만 필터
/// </summary>
public class Vector3KalmanFilter
{
    public float Q; // 시스템 예측 노이즈
    public float R; // 센서 측정 노이즈

    private Vector3 P = Vector3.one;  // 오차 공분산
    private Vector3 X = Vector3.zero; // 필터링된 현재 좌표
    private Vector3 K = Vector3.zero; // 칼만 이득(Gain)

    public Vector3KalmanFilter(float q, float r)
    {
        Q = q;
        R = r;
    }

    public void ResetState(Vector3 startPos)
    {
        X = startPos;
        P = Vector3.one;
    }

    public Vector3 Update(Vector3 measurement)
    {
        // 1. 오차 공분산 예측 (P = P + Q)
        P.x += Q; P.y += Q; P.z += Q;

        // 2. 칼만 이득 계산 (K = P / (P + R))
        K.x = P.x / (P.x + R);
        K.y = P.y / (P.y + R);
        K.z = P.z / (P.z + R);

        // 3. 현재 상태 업데이트 (X = X + K * (Measurement - X))
        X.x += K.x * (measurement.x - X.x);
        X.y += K.y * (measurement.y - X.y);
        X.z += K.z * (measurement.z - X.z);

        // 4. 오차 공분산 업데이트 (P = (1 - K) * P)
        P.x = (1f - K.x) * P.x;
        P.y = (1f - K.y) * P.y;
        P.z = (1f - K.z) * P.z;

        return X;
    }
}