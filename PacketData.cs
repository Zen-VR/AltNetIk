using LiteNetLib.Utils;
using System.Runtime.Serialization;

namespace AltNetIk
{
    public class PacketData : INetSerializable
    {
        public int photonId { get; set; }
        public int ping { get; set; }
        public bool frozen { get; set; }
        public short avatarKind { get; set; }
        [IgnoreDataMember]
        public int boneCount => boneRotations.Length;
        public UnityEngine.Vector3 hipPosition { get; set; }
        public UnityEngine.Quaternion hipRotation { get; set; }
        public UnityEngine.Vector3 playerPosition { get; set; }
        public UnityEngine.Quaternion playerRotation { get; set; }
        public bool[] boneList { get; set; }
        public UnityEngine.Quaternion[] boneRotations { get; set; }

        public void Serialize(NetDataWriter writer)
        {
            writer.Put(photonId);
            writer.Put(ping);
            writer.Put(frozen);
            writer.Put(avatarKind);

            Serializers.SerializeVector3(writer, hipPosition);
            Serializers.SerializeQuaternion(writer, hipRotation);
            Serializers.SerializeVector3(writer, playerPosition);
            Serializers.SerializeQuaternion(writer, playerRotation);
            Serializers.SerializePackedBoolArray(writer, boneList);
            Serializers.SerializeQuaternionArray(writer, boneRotations);
        }

        public void Deserialize(NetDataReader reader)
        {
            photonId = reader.GetInt();
            ping = reader.GetInt();
            frozen = reader.GetBool();
            avatarKind = reader.GetShort();

            hipPosition = Serializers.DeserializeVector3(reader);
            hipRotation = Serializers.DeserializeQuaternion(reader);
            playerPosition = Serializers.DeserializeVector3(reader);
            playerRotation = Serializers.DeserializeQuaternion(reader);
            boneList = Serializers.DeserializePackedBoolArray(reader);
            boneRotations = Serializers.DeserializeQuaternionArray(reader);
        }
    }

    public class ParamData : INetSerializable
    {
        public int photonId { get; set; }
        public string[] paramName { get; set; }
        public short[] paramType { get; set; }
        public bool[] boolParams { get; set; }
        public short[] intParams { get; set; }
        public float[] floatParams { get; set; }

        public void Serialize(NetDataWriter writer)
        {
            writer.Put(photonId);

            writer.PutArray(paramName);
            writer.PutArray(paramType);

            writer.PutArray(boolParams);
            writer.PutArray(intParams);
            writer.PutArray(floatParams);
        }

        public void Deserialize(NetDataReader reader)
        {
            photonId = reader.GetInt();

            paramName = reader.GetStringArray();
            paramType = reader.GetShortArray();

            boolParams = reader.GetBoolArray();
            intParams = reader.GetShortArray();
            floatParams = reader.GetFloatArray();
        }
    }

    public class EventData : INetSerializable
    {
        public short version { get; set; }
        public string lobbyHash { get; set; }
        public int photonId { get; set; }
        public string eventName { get; set; }
        public string data { get; set; }

        public void Serialize(NetDataWriter writer)
        {
            writer.Put(version);
            writer.Put(lobbyHash);
            writer.Put(photonId);
            writer.Put(eventName);
            writer.Put(data);
        }

        public void Deserialize(NetDataReader reader)
        {
            version = reader.GetShort();
            lobbyHash = reader.GetString();
            photonId = reader.GetShort();
            eventName = reader.GetString();
            data = reader.GetString();
        }
    }
}