using System;
using Nsc.Robots;
using Unity.Collections;
using Unity.Netcode;

namespace Nsc.Match
{
    /// <summary>ผู้เล่นคนนี้คุมแขนขาชิ้นนี้ของหุ่นตัวนี้</summary>
    public struct LimbAssignment : INetworkSerializable, IEquatable<LimbAssignment>
    {
        /// <summary>NetworkObjectId ของ Robot — ตรงกันทุกเครื่อง</summary>
        public ulong robotId;
        public LimbSlot slot;
        public ulong clientId;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref robotId);
            serializer.SerializeValue(ref slot);
            serializer.SerializeValue(ref clientId);
        }

        public bool Equals(LimbAssignment other) =>
            robotId == other.robotId && slot == other.slot && clientId == other.clientId;
    }

    /// <summary>
    /// ผู้เล่นหนึ่งคนในหน้าเลือก — ชื่อ ตัวตนถาวร (ใช้คืนที่นั่งตอนต่อกลับ) และหุ่นที่เลือก (= ทีมใน PVP)
    /// </summary>
    public struct SelectionPlayer : INetworkSerializable, IEquatable<SelectionPlayer>
    {
        public ulong clientId;
        public FixedString64Bytes playerName;
        /// <summary>
        /// ตัวตนที่ไม่เปลี่ยนข้ามการต่อใหม่ (AuthenticationService.PlayerId) — NGO แจก clientId ใหม่ทุกครั้งที่ต่อ
        /// </summary>
        public FixedString64Bytes playerId;
        /// <summary>false = หลุดไปแล้วแต่ยังกันที่ไว้ให้ รอกลับเข้ามา</summary>
        public bool connected;
        /// <summary>หุ่นที่เลือกอยู่ (0 = ยังไม่เลือก)</summary>
        public ulong robotId;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref clientId);
            serializer.SerializeValue(ref playerName);
            serializer.SerializeValue(ref playerId);
            serializer.SerializeValue(ref connected);
            serializer.SerializeValue(ref robotId);
        }

        public bool Equals(SelectionPlayer other) =>
            clientId == other.clientId && playerName.Equals(other.playerName) &&
            playerId.Equals(other.playerId) && connected == other.connected && robotId == other.robotId;

        public string DisplayName
        {
            get
            {
                string name = playerName.ToString();
                return string.IsNullOrEmpty(name) ? "Player" : name;
            }
        }
    }
}
