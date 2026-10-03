using UnityEngine;

namespace Jogging.World
{
    /// <summary>
    /// What a companion dog prefab offers (filled by Editor/DogAssets): its legacy Animation with in-place clips
    /// and their natural ground speeds (m/s), so the paws fit the ground at any speed (World/DogCompanion).
    /// </summary>
    public class DogRig : MonoBehaviour
    {
        public Animation anim;
        public AnimationClip idle, play, walk, walkLeft, walkRight, run, runLeft, runRight;
        public float walkSpeed = 1.3f, runSpeed = 6f, lengthM = 1.1f;
    }
}
