using LiteNetLib.Utils;

namespace AltNetIk
{
    public class PacketData : INetSerializable
    {
        public struct Vector3 : INetSerializable
        {
            public float x;
            public float y;
            public float z;

            public Vector3(UnityEngine.Vector3 vector3)
            {
                x = vector3.x;
                y = vector3.y;
                z = vector3.z;
            }

            public static implicit operator Vector3(UnityEngine.Vector3 a) => new Vector3(a);

            public static implicit operator UnityEngine.Vector3(Vector3 a) => new UnityEngine.Vector3(a.x, a.y, a.z);

            public void Serialize(NetDataWriter writer)
            {
                writer.Put(x);
                writer.Put(y);
                writer.Put(z);
            }

            public void Deserialize(NetDataReader reader)
            {
                x = reader.GetFloat();
                y = reader.GetFloat();
                z = reader.GetFloat();
            }
        }

        public struct Quaternion : INetSerializable
        {
            public float x;
            public float y;
            public float z;
            public float w;

            public Quaternion(UnityEngine.Quaternion quaternion)
            {
                x = quaternion.x;
                y = quaternion.y;
                z = quaternion.z;
                w = quaternion.w;
            }

            public Quaternion(float X, float Y, float Z, float W)
            {
                this.x = X;
                this.y = Y;
                this.z = Z;
                this.w = W;
            }

            public static implicit operator Quaternion(UnityEngine.Quaternion a) => new Quaternion(a);

            public static implicit operator UnityEngine.Quaternion(Quaternion a) => new UnityEngine.Quaternion(a.x, a.y, a.z, a.w);

            public void Serialize(NetDataWriter writer)
            {
                writer.Put(x);
                writer.Put(y);
                writer.Put(z);
                writer.Put(w);
            }

            public void Deserialize(NetDataReader reader)
            {
                x = reader.GetFloat();
                y = reader.GetFloat();
                z = reader.GetFloat();
                w = reader.GetFloat();
            }
        }

        public int photonId { get; set; }
        public int ping { get; set; }
        public bool frozen { get; set; }
        public short avatarKind { get; set; }
        public byte boneCount { get; set; }
        public Vector3 hipPosition { get; set; }
        public Quaternion hipRotation { get; set; }
        public Vector3 playerPosition { get; set; }
        public Quaternion playerRotation { get; set; }
        public bool[] boneList { get; set; }
        public Quaternion[] boneRotations { get; set; }

        public void Serialize(NetDataWriter writer)
        {
            writer.Put(photonId);
            writer.Put(ping);
            writer.Put(frozen);
            writer.Put(avatarKind);
            writer.Put(boneCount);

            writer.Put(hipPosition);
            writer.Put(hipRotation);
            writer.Put(playerPosition);
            writer.Put(playerRotation);

            writer.PutArray(boneList);

            foreach (Quaternion boneRotation in boneRotations)
            {
                writer.Put(boneRotation);
            }
        }

        public void Deserialize(NetDataReader reader)
        {
            photonId = reader.GetInt();
            ping = reader.GetInt();
            frozen = reader.GetBool();
            avatarKind = reader.GetShort();
            boneCount = reader.GetByte();

            hipPosition = reader.Get<Vector3>();
            hipRotation = reader.Get<Quaternion>();
            playerPosition = reader.Get<Vector3>();
            playerRotation = reader.Get<Quaternion>();

            boneList = reader.GetBoolArray();

            boneRotations = new Quaternion[boneCount];
            for (int i = 0; i < boneCount; i++)
            {
                boneRotations[i] = reader.Get<Quaternion>();
            }
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