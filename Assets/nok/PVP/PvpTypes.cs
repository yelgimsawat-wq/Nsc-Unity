using System;
using Unity.Collections;
using Unity.Netcode;

namespace NscGame.Pvp
{
    /// <summary>ทีมในโหมด PVP — None = ยังไม่เลือกทีม</summary>
    public enum PvpTeam : byte
    {
        None = 0,
        Red  = 1,
        Blue = 2
    }

    /// <summary>สถานะแมตช์ — sync ให้ทุกเครื่องรู้ตรงกันว่าตอนนี้อยู่ช่วงไหน</summary>
    public enum PvpMatchState : byte
    {
        TeamSelect = 0, // กำลังเลือกทีม/ชิ้นส่วน
        Fighting   = 1, // สู้กันอยู่ (นับดาเมจเฉพาะช่วงนี้)
        Finished   = 2  // จบแมตช์ มีผู้ชนะแล้ว
    }

    /// <summary>
    /// ข้อมูลผู้เล่นหนึ่งคนในแมตช์ PVP — เก็บ clientId / ทีม / ชิ้นส่วนที่จอง / ชื่อ ไว้ใน struct เดียว
    /// (ใช้ struct เดียวแทน list คู่ขนานหลายอัน กันหลุด sync กันเอง — แนวเดียวกับ LobbyManager.LobbyPlayer)
    /// </summary>
    public struct PvpPlayerEntry : INetworkSerializable, IEquatable<PvpPlayerEntry>
    {
        public ulong clientId;
        public PvpTeam team;
        /// <summary>ชิ้นส่วนที่จองไว้ในทีมตัวเอง 0-3 (ดู <see cref="PvpLimb"/>) — -1 = ยังไม่จอง</summary>
        public int limbIndex;
        public FixedString64Bytes playerName;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref clientId);
            serializer.SerializeValue(ref team);
            serializer.SerializeValue(ref limbIndex);
            serializer.SerializeValue(ref playerName);
        }

        public bool Equals(PvpPlayerEntry other) =>
            clientId == other.clientId &&
            team == other.team &&
            limbIndex == other.limbIndex &&
            playerName.Equals(other.playerName);

        public static PvpPlayerEntry Create(ulong clientId, string name = "Player") => new PvpPlayerEntry
        {
            clientId   = clientId,
            team       = PvpTeam.None,
            limbIndex  = -1,
            playerName = name
        };
    }

    /// <summary>ค่าคงที่ของ index ชิ้นส่วน — ใช้ร่วมกันทุกสคริปต์ PVP จะได้ไม่หลุด order กันเอง</summary>
    public static class PvpLimb
    {
        public const int Count    = 4;
        public const int LeftArm  = 0;
        public const int RightArm = 1;
        public const int LeftLeg  = 2;
        public const int RightLeg = 3;

        public static string Name(int index) => index switch
        {
            LeftArm  => "Left Arm",
            RightArm => "Right Arm",
            LeftLeg  => "Left Leg",
            RightLeg => "Right Leg",
            _        => "?"
        };

        public static bool IsArm(int index) => index == LeftArm || index == RightArm;
        public static bool IsValid(int index) => index >= 0 && index < Count;
    }

    public static class PvpTeamUtil
    {
        public static PvpTeam Opposite(this PvpTeam team) => team switch
        {
            PvpTeam.Red  => PvpTeam.Blue,
            PvpTeam.Blue => PvpTeam.Red,
            _            => PvpTeam.None
        };

        public static string DisplayName(this PvpTeam team) => team switch
        {
            PvpTeam.Red  => "RED",
            PvpTeam.Blue => "BLUE",
            _            => "NONE"
        };

        public static UnityEngine.Color DisplayColor(this PvpTeam team) => team switch
        {
            PvpTeam.Red  => new UnityEngine.Color(0.90f, 0.22f, 0.24f, 1f),
            PvpTeam.Blue => new UnityEngine.Color(0.20f, 0.55f, 0.95f, 1f),
            _            => new UnityEngine.Color(0.6f, 0.6f, 0.6f, 1f)
        };
    }
}
