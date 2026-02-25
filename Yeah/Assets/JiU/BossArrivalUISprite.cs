using UnityEngine;
using UnityEngine.UI;

namespace JiU
{
    /// <summary>
    /// Boss 到达时把指定 UI Image 的 Sprite 换成“Boss 图”，Boss 离开后换回原来的图。
    /// 依赖 GameManager 的 OnBossArrived / OnBossLeft 事件。
    /// </summary>
    public class BossArrivalUISprite : MonoBehaviour
    {
        [Header("UI Image")]
        [Tooltip("要更换 Sprite 的 Image 组件")]
        public Image targetImage;

        [Tooltip("Boss 在场时显示的 Sprite")]
        public Sprite spriteWhenBossHere;

        [Tooltip("Boss 离开后显示的 Sprite；不填则使用 Image 当前的 Sprite 作为默认")]
        public Sprite spriteWhenBossGone;

        private Sprite _initialSprite;

        void Start()
        {
            if (targetImage != null)
                _initialSprite = targetImage.sprite;

            if (GameManager.Instance == null) return;

            GameManager.Instance.OnBossArrived.AddListener(SetBossSprite);
            GameManager.Instance.OnBossLeft.AddListener(SetNormalSprite);
        }

        void OnDestroy()
        {
            if (GameManager.Instance == null) return;
            GameManager.Instance.OnBossArrived.RemoveListener(SetBossSprite);
            GameManager.Instance.OnBossLeft.RemoveListener(SetNormalSprite);
        }

        /// <summary> Boss 到达时换图（由事件调用） </summary>
        public void SetBossSprite()
        {
            if (targetImage == null || spriteWhenBossHere == null) return;
            targetImage.sprite = spriteWhenBossHere;
        }

        /// <summary> Boss 离开后换回（由事件调用） </summary>
        public void SetNormalSprite()
        {
            if (targetImage == null) return;
            targetImage.sprite = spriteWhenBossGone != null ? spriteWhenBossGone : _initialSprite;
        }
    }
}
