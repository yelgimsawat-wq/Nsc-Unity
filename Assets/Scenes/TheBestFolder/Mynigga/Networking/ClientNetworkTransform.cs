using Unity.Netcode.Components;
using UnityEngine;

namespace Unity.Multiplayer.Samples.Utilities.ClientAuthority
{
    [DisallowMultipleComponent]
    public class ClientNetworkTransform : NetworkTransform
    {
        // This is the magic line. It tells the network to trust the owner of the object (the client)
        // instead of strictly trusting the server.
        protected override bool OnIsServerAuthoritative()
        {
            return false;
        }
    }
}