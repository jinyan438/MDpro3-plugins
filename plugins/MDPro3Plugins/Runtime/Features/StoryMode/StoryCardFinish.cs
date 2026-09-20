using System;
using MDPro3.UI;
using MDPro3.Duel.YGOSharp;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace MDPro3.Plugins.Features.StoryMode
{
    // A finish belongs to this physical card image, never to the global Rarity.json.
    public sealed class StoryCardFinish : MonoBehaviour
    {
        [ThreadStatic] private static StoryRarity? materialRarity;
        public StoryRarity Rarity { get; private set; }
        internal StoryDeckCardVersion DeckCopy;
        private CardRawImageHandler handler;
        private Material material;
        private int loadedCode;
        private StoryRarity loadedRarity;
        private StoryCardFoil cardFoil;

        internal static StoryCardFinish Apply(CardRawImageHandler target, StoryRarity rarity)
        {
            if (target == null) return null;
            var finish = target.GetComponent<StoryCardFinish>() ?? target.gameObject.AddComponent<StoryCardFinish>();
            finish.handler = target; finish.Rarity = rarity;
            if (rarity == StoryRarity.SR && finish.cardFoil == null)
            {
                var foil = StoryUI.Rect("SuperRareCardFoil", target.transform, 0, 0, 1, 1);
                finish.cardFoil = foil.gameObject.AddComponent<StoryCardFoil>();
                finish.cardFoil.raycastTarget = false;
            }
            if (finish.cardFoil != null) finish.cardFoil.gameObject.SetActive(rarity == StoryRarity.SR && target.Refreshed);
            return finish;
        }

        // Called only in MaterialLoader.GetCardMaterial by the plugin's build-time hook.
        // SR uses ordinary printing underneath its separate full-face foil.
        public static CardRarity.Rarity ResolveMaterialRarity(int code) => materialRarity.HasValue
            ? materialRarity.Value == StoryRarity.SR ? CardRarity.Rarity.Normal : (CardRarity.Rarity)(int)materialRarity.Value
            : CardRarity.GetRarity(code);

        internal static Material CreateMaterial(int code, StoryRarity rarity, bool use3D = false)
        {
            var previous = materialRarity;
            try
            {
                materialRarity = rarity;
                return MaterialLoader.GetCardMaterial(code, use3D);
            }
            finally { materialRarity = previous; }
        }

        internal static void ShowDetail(StoryCard card)
        {
            var owner = UIManager.InputBlocker;
            var handle = Addressables.InstantiateAsync("UIWidges/CardInfoDetail.prefab");
            handle.Completed += result =>
            {
                if (result.Status != AsyncOperationStatus.Succeeded) return;
                if (owner == null || !owner.isActiveAndEnabled)
                { Addressables.ReleaseInstance(result); return; }
                result.Result.transform.SetParent(PluginGame.UI.popup, false);
                result.Result.GetComponent<CardInfoDetail>().Show(CardsManager.Get(card.id));
                foreach (var image in result.Result.GetComponentsInChildren<CardRawImageHandler>(true))
                    if (image.card?.Id == card.id) Apply(image, card.rarity);
            };
        }

        private void LateUpdate()
        {
            if (cardFoil != null)
            {
                cardFoil.gameObject.SetActive(Rarity == StoryRarity.SR && handler != null && handler.card != null && handler.Refreshed);
                if (cardFoil.isActiveAndEnabled)
                    cardFoil.color = new Color(1, 1, 1, handler.RawImage.color.a);
            }
            if (handler == null || handler.card == null || !handler.Refreshed) return;
            if (material == null || loadedCode != handler.card.Id || loadedRarity != Rarity)
            {
                if (material != null) Destroy(material);
                material = CreateMaterial(handler.card.Id, Rarity);
                loadedCode = handler.card.Id; loadedRarity = Rarity;
                material.SetFloat("_LoadingBlend", 0);
            }
            if (handler.RawImage.material != material) handler.RawImage.material = material;
        }

        internal static Color Tint(StoryRarity rarity)
        {
            switch (rarity)
            {
                case StoryRarity.R: return new Color(.3f, .8f, 1);
                case StoryRarity.SR: return new Color(.84f, .9f, 1);
                case StoryRarity.UR: return new Color(.79f, .65f, 1);
                case StoryRarity.GR: return new Color(1, .77f, .32f);
                case StoryRarity.MR: return new Color(1, .43f, .7f);
                default: return new Color(.65f, .74f, .82f);
            }
        }
        private void OnDestroy() { if (material != null) Destroy(material); }
    }
}
