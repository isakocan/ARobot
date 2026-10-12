using UnityEngine;

namespace ARaki.AR
{
    /// <summary>
    /// Dünyadaki görünmez bir noktanın (ör. bir marker'ın yeri) AR görselini, robota göre konumuyla senkronlar.
    /// Nokta pasifleşince görseli de gizlenir.
    /// </summary>
    public class ARMirror : MonoBehaviour
    {
        [SerializeField] ARRig rig;
        [SerializeField] Transform visual;

        public Transform Visual => visual;

        /// <summary>Henüz keşfedilmemiş nesne: görseli gizli kalır.</summary>
        public bool Hidden
        {
            get => hidden;
            set
            {
                hidden = value;
                if (visual != null && isActiveAndEnabled) visual.gameObject.SetActive(!hidden);
            }
        }

        bool hidden;

        void OnEnable()
        {
            if (visual != null) visual.gameObject.SetActive(!hidden);
            Sync();
        }

        void OnDisable()
        {
            if (visual != null) visual.gameObject.SetActive(false);
        }

        void LateUpdate() => Sync();

        public void Sync()
        {
            if (rig == null || visual == null) return;
            visual.position = rig.ToRig(transform.position);
            visual.rotation = rig.ToRig(transform.rotation);
        }
    }
}
