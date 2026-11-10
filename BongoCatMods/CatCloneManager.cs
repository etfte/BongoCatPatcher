using System;
using System.Collections.Generic;
using System.Linq;
using BongoCat;
using UnityEngine;

namespace BongoCatMods
{
    public static class CatCloneManager
    {
        private static readonly List<Cat> _clones = new List<Cat>();

        public static void Update()
        {
            try
            {
                // F7 = Спавн нового кота-клона
                if (Input.GetKeyDown(KeyCode.F7))
                {
                    SpawnClone();
                }

                // F5 = Удалить последнего клона
                if (Input.GetKeyDown(KeyCode.F5))
                {
                    RemoveLastClone();
                }

                // F9 = Перемешать скины у всех котиков
                if (Input.GetKeyDown(KeyCode.F9))
                {
                    RandomizeAllClones();
                }

                // F10 = Мгновенная разблокировка и сбор наград PawPass
                if (Input.GetKeyDown(KeyCode.F10))
                {
                    UnlockAndClaimPawPass();
                }
            }
            catch (Exception ex)
            {
                Debug.LogError("[CatCloneManager] Ошибка в Update: " + ex);
            }
        }

        public static void SpawnClone()
        {
            try
            {
                if (Cat.Instance == null)
                {
                    Debug.LogWarning("[CatCloneManager] Cat.Instance не найден, спавн невозможен.");
                    return;
                }

                _clones.RemoveAll(c => c == null);

                // Клонируем основной объект кота
                GameObject sourceObj = Cat.Instance.gameObject;
                Transform parent = sourceObj.transform.parent != null ? sourceObj.transform.parent : sourceObj.transform;
                GameObject cloneGo = UnityEngine.Object.Instantiate(sourceObj, parent);
                cloneGo.name = "BongoCat_Clone_" + (_clones.Count + 1);

                Cat cloneCat = cloneGo.GetComponent<Cat>();
                if (cloneCat == null)
                {
                    Debug.LogWarning("[CatCloneManager] Клонированный объект не содержит компонент Cat.");
                    return;
                }

                // Смещаем позицию клона в сторону, чтобы коты красиво стояли в ряд
                int index = _clones.Count + 1;
                float spacing = 160f;
                float offsetX = (index % 6) * spacing;
                float offsetY = -(index / 6) * 45f;

                cloneGo.transform.position = Cat.Instance.transform.position + new Vector3(offsetX, offsetY, 0);

                // Подписываем клона на событие тапа лапками от главного кота
                if (Cat.Instance.OnTap != null)
                {
                    Cat.Instance.OnTap.AddListener(cloneCat.Tap);
                }

                // Случайно наряжаем нового кота из доступного гардероба
                RandomizeCat(cloneCat);

                _clones.Add(cloneCat);
                Debug.Log($"[CatCloneManager] Заспавнен кот #{_clones.Count}! Всего котов: {_clones.Count + 1}. [F7: еще, F5: убрать, F9: смена скинов]");
            }
            catch (Exception ex)
            {
                Debug.LogError("[CatCloneManager] Ошибка SpawnClone: " + ex);
            }
        }

        public static void RemoveLastClone()
        {
            try
            {
                _clones.RemoveAll(c => c == null);
                if (_clones.Count == 0)
                {
                    Debug.Log("[CatCloneManager] Дополнительных котов для удаления нет.");
                    return;
                }

                int lastIdx = _clones.Count - 1;
                Cat cat = _clones[lastIdx];
                _clones.RemoveAt(lastIdx);

                if (cat != null)
                {
                    if (Cat.Instance != null && Cat.Instance.OnTap != null)
                    {
                        Cat.Instance.OnTap.RemoveListener(cat.Tap);
                    }
                    UnityEngine.Object.Destroy(cat.gameObject);
                    Debug.Log($"[CatCloneManager] Удален клон кота. Осталось клонов: {_clones.Count}");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError("[CatCloneManager] Ошибка RemoveLastClone: " + ex);
            }
        }

        public static void RandomizeAllClones()
        {
            try
            {
                _clones.RemoveAll(c => c == null);
                foreach (var cat in _clones)
                {
                    if (cat != null)
                    {
                        RandomizeCat(cat);
                    }
                }
                Debug.Log($"[CatCloneManager] Обновлены скины для всех {_clones.Count} клонов!");
            }
            catch (Exception ex)
            {
                Debug.LogError("[CatCloneManager] Ошибка RandomizeAllClones: " + ex);
            }
        }

        public static void RandomizeCat(Cat cat)
        {
            try
            {
                if (cat == null || CatInventory.Instance == null || CatInventory.Instance.Items == null) return;

                var items = CatInventory.Instance.Items;
                var hats = items.Where(i => i != null && i.IsHat).ToList();
                var skins = items.Where(i => i != null && i.IsSkin).ToList();
                var bongos = items.Where(i => i != null && i.IsBongos).ToList();

                if (hats.Count > 0)
                {
                    var hat = hats[UnityEngine.Random.Range(0, hats.Count)];
                    cat.SetItem(hat, false);
                }

                if (skins.Count > 0)
                {
                    var skin = skins[UnityEngine.Random.Range(0, skins.Count)];
                    cat.SetItem(skin, false);
                }

                if (bongos.Count > 0)
                {
                    var bongo = bongos[UnityEngine.Random.Range(0, bongos.Count)];
                    cat.SetItem(bongo, false);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError("[CatCloneManager] Ошибка RandomizeCat: " + ex);
            }
        }

        public static void UnlockAndClaimPawPass()
        {
            try
            {
                var mgr = UnityEngine.Object.FindAnyObjectByType<BongoCat.PawPass.PawPassManager>();
                if (mgr != null)
                {
                    mgr.UnlockPremiumPass();
                    mgr.ClaimAll();
                    Debug.Log("[CatCloneManager] Все награды PawPass успешно собраны в 1 клик!");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError("[CatCloneManager] Ошибка UnlockAndClaimPawPass: " + ex);
            }
        }
    }
}
