using System;
using UnityEngine;

namespace FestivalDerby.Driving.Runtime
{
    public sealed class OwnedCarGeometry : IDisposable
    {
        private readonly PhysicsMaterial _floorMaterial;
        private readonly PhysicsMaterial _chassisMaterial;
        internal OwnedCarGeometry(GameObject root, Rigidbody body, BoxCollider chassis,
            BoxCollider floor, Material material, PhysicsMaterial floorMaterial = null,
            PhysicsMaterial chassisMaterial = null)
        {
            Root = root;
            Body = body;
            Chassis = chassis;
            Floor = floor;
            Material = material;
            _floorMaterial = floorMaterial;
            _chassisMaterial = chassisMaterial;
        }

        public GameObject Root { get; }
        public Rigidbody Body { get; }
        public BoxCollider Chassis { get; }
        public BoxCollider Floor { get; }
        public Material Material { get; }
        public bool IsDisposed { get; private set; }

        public void Dispose()
        {
            if (IsDisposed)
                return;
            IsDisposed = true;
            if (Root != null)
            {
                Root.SetActive(false);
                UnityEngine.Object.Destroy(Root);
            }
            if (Material != null)
                UnityEngine.Object.Destroy(Material);
            if (_floorMaterial != null)
                UnityEngine.Object.Destroy(_floorMaterial);
            if (_chassisMaterial != null)
                UnityEngine.Object.Destroy(_chassisMaterial);
        }
    }
}
