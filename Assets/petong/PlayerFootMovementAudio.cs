using UnityEngine;
using Unity.Netcode;

// 🔊 [Foot Movement + Landing Audio] 別スクリプト、PlayerFootForRobot.cs には触れない
// PlayerFootForRobot と同じ GameObject に付ける
public class PlayerFootMovementAudio : NetworkBehaviour
{
    [Header("Reference")]
    [Tooltip("同じ object 上の PlayerFootForRobot をここへドラッグ")]
    public PlayerFootForRobot foot;

    [Header("Landing Sound (One-Shot)")]
    [Tooltip("ドラッグを離して足が地面に着いた瞬間の音")]
    public AudioSource landingAudioSource;
    public AudioClip footLandingClip;
    [Range(0f, 1f)] public float landingVolume = 0.8f;
    public float landingPitchVariation = 0.05f;

    [Header("Movement Sound (Loop while dragging)")]
    [Tooltip("ドラッグ中ずっと鳴らすループ音（着地音とは別音源）")]
    public AudioSource movementAudioSource;
    public AudioClip footMovementClip;
    [Range(0f, 1f)] public float movementVolume = 0.5f;
    public float movementFadeTime = 0.08f;

    private float _movementTargetVolume;
    private float _movementFadeVel;

    // 他プレイヤー用の同期状態（サーバーが権威）
    private readonly NetworkVariable<bool> _steppingSynced =
        new NetworkVariable<bool>(false, NetworkVariableReadPermission.Everyone);

    // オーナー自身の「前フレームの値」— ネットワーク同期を待たず即座に検知するため
    private bool _localPrevStepping;

    private void Awake()
    {
        if (movementAudioSource != null)
        {
            movementAudioSource.loop = true;
            movementAudioSource.playOnAwake = false;
            movementAudioSource.spatialBlend = 1f;
            movementAudioSource.volume = 0f;
            movementAudioSource.clip = footMovementClip;
        }

        if (landingAudioSource != null)
        {
            landingAudioSource.loop = false;
            landingAudioSource.playOnAwake = false;
            landingAudioSource.spatialBlend = 1f;
        }
    }

    public override void OnNetworkSpawn()
    {
        // 他プレイヤー（オーナーではない）はここから音を鳴らす
        _steppingSynced.OnValueChanged += OnSteppingChangedRemote;
        _localPrevStepping = foot != null && foot.isStepping;
    }

    public override void OnNetworkDespawn()
    {
        _steppingSynced.OnValueChanged -= OnSteppingChangedRemote;
    }

    private void Update()
    {
        if (foot == null) return;

        // ✅ [Zero-Latency Path] オーナーはネットワーク同期を待たず、ローカルの isStepping を直接見る
        // isStepping は HandleInput 内でオーナーのクライアント上で即座に true/false になっているため、
        // NetworkVariable の往復（送信→サーバー確定→同期戻り）を待つより確実に速い
        if (IsOwner)
        {
            bool isDraggingNow = foot.isStepping && !foot.IsKickControllingFoot;
            if (_localPrevStepping && !isDraggingNow) PlayLandingSound();
            SetMovementLoop(isDraggingNow);
            _localPrevStepping = isDraggingNow;
        }

        // サーバーは自分の状態を NetworkVariable に反映 → 他クライアントへ配信
        if (IsServer)
        {
            bool serverStepping = foot.isStepping && !foot.IsKickControllingFoot;
            if (_steppingSynced.Value != serverStepping)
                _steppingSynced.Value = serverStepping;
        }

        // ループ音のフェード処理は全員（オーナー含む）で毎フレーム更新
        if (movementAudioSource != null)
        {
            movementAudioSource.volume = Mathf.SmoothDamp(
                movementAudioSource.volume, _movementTargetVolume, ref _movementFadeVel, movementFadeTime);
            if (movementAudioSource.volume <= 0.001f && movementAudioSource.isPlaying && _movementTargetVolume == 0f)
                movementAudioSource.Stop();
        }
    }

    // 他プレイヤー（オーナーではない自分の画面）用 — NetworkVariable 経由でしか状態を知れない
    private void OnSteppingChangedRemote(bool oldValue, bool newValue)
    {
        if (IsOwner) return; // オーナーは上の Update() のローカル即時パスで既に処理済み

        if (oldValue == true && newValue == false) PlayLandingSound();
        SetMovementLoop(newValue);
    }

    private void SetMovementLoop(bool isDragging)
    {
        if (movementAudioSource == null || footMovementClip == null) return;
        _movementTargetVolume = isDragging ? movementVolume : 0f;
        if (isDragging && !movementAudioSource.isPlaying) movementAudioSource.Play();
    }

    private void PlayLandingSound()
    {
        if (landingAudioSource == null || footLandingClip == null) return;
        landingAudioSource.pitch = 1f + Random.Range(-landingPitchVariation, landingPitchVariation);
        landingAudioSource.PlayOneShot(footLandingClip, landingVolume);
    }
}