using System;
using UnityEngine;
using UnityEngine.Splines;
using Zenject;

namespace Pixel
{
    public class GameSceneInstaller : MonoInstaller
    {
        [SerializeField] private SplineContainer splineContainer;
        [SerializeField] private RenderMap renderMap;
        [SerializeField] private SlotReturn[] slotReturns;
        
        public override void InstallBindings()
        {
            if (slotReturns != null)
                Array.Sort(slotReturns, CompareSlotReturns);

            Container.Bind<SplineContainer>()
                .FromInstance(splineContainer)
                .AsSingle();

            Container.Bind<RenderMap>()
                .FromInstance(renderMap)
                .AsSingle();

            Container.Bind<SlotReturn[]>()
                .FromInstance(slotReturns)
                .AsSingle();
        }

        private int CompareSlotReturns(SlotReturn a, SlotReturn b)
        {
            if (a == null && b == null) return 0;
            if (a == null) return 1;
            if (b == null) return -1;

            int idCompare = a.SlotId.CompareTo(b.SlotId);
            if (idCompare != 0) return idCompare;

            return string.CompareOrdinal(a.name, b.name);
        }
    }
}