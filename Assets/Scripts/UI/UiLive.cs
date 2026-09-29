using System;
using UnityEngine;

namespace Jogging.UI
{
    /// <summary>Runs an update callback every frame (also while the menu freezes time), e.g. for live labels.</summary>
    public class UiLive : MonoBehaviour
    {
        private Action tick;

        public static void Attach(GameObject go, Action onUpdate)
        {
            var l = go.AddComponent<UiLive>();
            l.tick = onUpdate;
            onUpdate();
        }

        private void Update() => tick?.Invoke();
    }
}
