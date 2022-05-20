using LiteNetLib;
using LiteNetLib.Utils;
using System.Runtime.CompilerServices;
using SysQuat = System.Numerics.Quaternion;
using SysVec3 = System.Numerics.Vector3;
using UniQuat = UnityEngine.Quaternion;
using UniVec3 = UnityEngine.Vector3;

namespace AltNetIk
{
    internal static class Extensions
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static UniVec3 ToUnity(this SysVec3 vector)
        {
            return new UniVec3
            {
                x = vector.X,
                y = vector.Y,
                z = vector.Z
            };
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static SysVec3 ToSystem(this UniVec3 vector)
        {
            return new SysVec3(vector.x, vector.y, vector.z);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static UniQuat ToUnity(this SysQuat quaternion)
        {
            return new UniQuat
            {
                x = quaternion.X,
                y = quaternion.Y,
                z = quaternion.Z,
                w = quaternion.W
            };
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static SysQuat ToSystem(this UniQuat quaternion)
        {
            return new SysQuat(quaternion.x, quaternion.y, quaternion.z, quaternion.w);
        }
    
        public static void Send(this NetPeer netPeer, NetDataWriter writer)
        {
            netPeer.Send(writer, writer.Length > netPeer.Mtu ? DeliveryMethod.ReliableOrdered : DeliveryMethod.Unreliable);
        }
    }
}
