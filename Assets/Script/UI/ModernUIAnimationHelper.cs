using System;
using System.Collections;
using UnityEngine;

namespace AsadoSimulator.UI
{
    /// <summary>
    /// 现代化 UI 动画平滑缓动辅助类 (ModernUIAnimationHelper)
    /// 特性：
    /// 1. 严格全量使用 Time.unscaledDeltaTime，在 Time.timeScale = 0（单机完全暂停）时绝对不会卡死！
    /// 2. 具备零除保护、防卡死超时保护与即时恢复机制。
    /// 3. 提供 Elastic Pop-In（弹性回弹）与 Smooth Fade（丝滑淡入淡出）。
    /// </summary>
    public static class ModernUIAnimationHelper
    {
        /// <summary>
        /// 缓动曲线函数：EaseOutBack (弹性回弹过冲)
        /// </summary>
        public static float EaseOutBack(float t, float overshoot = 1.35f)
        {
            t = Mathf.Clamp01(t) - 1.0f;
            return t * t * ((overshoot + 1f) * t + overshoot) + 1.0f;
        }

        /// <summary>
        /// 缓动曲线函数：EaseInOutCubic (平滑进出过渡)
        /// </summary>
        public static float EaseInOutCubic(float t)
        {
            t = Mathf.Clamp01(t);
            return t < 0.5f ? 4f * t * t * t : 1f - Mathf.Pow(-2f * t + 2f, 3f) / 2f;
        }

        /// <summary>
        /// 现代化弹窗弹出动画 (弹性回弹 + 透明度淡入)
        /// 严格使用 Time.unscaledDeltaTime，保证暂停时 100% 正常播放！
        /// </summary>
        public static IEnumerator AnimatePopIn(RectTransform rect, CanvasGroup group, float duration = 0.25f, Action onComplete = null)
        {
            if (rect == null && group == null)
            {
                onComplete?.Invoke();
                yield break;
            }

            // 极速/零时长保护：直接显示最终状态
            if (duration <= 0.001f)
            {
                if (rect != null) rect.localScale = Vector3.one;
                if (group != null)
                {
                    group.alpha = 1f;
                    group.blocksRaycasts = true;
                    group.interactable = true;
                }
                onComplete?.Invoke();
                yield break;
            }

            if (group != null)
            {
                group.alpha = 0f;
                group.blocksRaycasts = true;
                group.interactable = true;
            }

            if (rect != null)
            {
                rect.localScale = new Vector3(0.85f, 0.85f, 1f);
            }

            float elapsed = 0f;
            while (elapsed < duration)
            {
                // 使用 unscaledDeltaTime，并限制单帧最大步进防止掉帧突变
                float dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
                elapsed += dt;
                float progress = Mathf.Clamp01(elapsed / duration);

                if (rect != null)
                {
                    float scaleFactor = EaseOutBack(progress, 1.25f);
                    rect.localScale = Vector3.one * Mathf.LerpUnclamped(0.85f, 1.0f, scaleFactor);
                }

                if (group != null)
                {
                    group.alpha = Mathf.SmoothStep(0f, 1f, progress * 1.4f);
                }

                yield return null;
            }

            // 确保最终状态必定为完全可见与可交互
            if (rect != null) rect.localScale = Vector3.one;
            if (group != null)
            {
                group.alpha = 1f;
                group.blocksRaycasts = true;
                group.interactable = true;
            }
            onComplete?.Invoke();
        }

        /// <summary>
        /// 现代化弹窗收起动画 (缩小 + 淡出)
        /// </summary>
        public static IEnumerator AnimatePopOut(RectTransform rect, CanvasGroup group, float duration = 0.16f, Action onComplete = null)
        {
            if (rect == null && group == null)
            {
                onComplete?.Invoke();
                yield break;
            }

            if (group != null)
            {
                group.blocksRaycasts = false;
                group.interactable = false;
            }

            if (duration <= 0.001f)
            {
                if (rect != null) rect.localScale = Vector3.one * 0.9f;
                if (group != null) group.alpha = 0f;
                onComplete?.Invoke();
                yield break;
            }

            float elapsed = 0f;
            while (elapsed < duration)
            {
                float dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
                elapsed += dt;
                float progress = Mathf.Clamp01(elapsed / duration);

                float ease = EaseInOutCubic(progress);
                if (rect != null)
                {
                    rect.localScale = Vector3.one * Mathf.Lerp(1.0f, 0.88f, ease);
                }

                if (group != null)
                {
                    group.alpha = Mathf.Lerp(1f, 0f, ease);
                }

                yield return null;
            }

            if (rect != null) rect.localScale = Vector3.one * 0.88f;
            if (group != null) group.alpha = 0f;
            onComplete?.Invoke();
        }

        /// <summary>
        /// 平滑淡入
        /// </summary>
        public static IEnumerator AnimateFade(CanvasGroup group, float targetAlpha, float duration = 0.2f, Action onComplete = null)
        {
            if (group == null)
            {
                onComplete?.Invoke();
                yield break;
            }

            if (duration <= 0.001f)
            {
                group.alpha = targetAlpha;
                group.blocksRaycasts = targetAlpha > 0.01f;
                group.interactable = targetAlpha > 0.01f;
                onComplete?.Invoke();
                yield break;
            }

            float startAlpha = group.alpha;
            float elapsed = 0f;

            while (elapsed < duration)
            {
                float dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
                elapsed += dt;
                float progress = Mathf.Clamp01(elapsed / duration);
                group.alpha = Mathf.Lerp(startAlpha, targetAlpha, EaseInOutCubic(progress));
                yield return null;
            }

            group.alpha = targetAlpha;
            group.blocksRaycasts = targetAlpha > 0.01f;
            group.interactable = targetAlpha > 0.01f;
            onComplete?.Invoke();
        }
    }
}
