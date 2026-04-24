using System;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

[Serializable]
public class AudioClipSet
{
    public AudioClip[] clips;

    private int lastIndex = -1;

    public AudioClip GetRandomClip()
    {
        if (clips == null || clips.Length == 0)
        {
            return null;
        }

        if (clips.Length == 1)
        {
            lastIndex = 0;
            return clips[0];
        }

        int index = UnityEngine.Random.Range(0, clips.Length);
        if (index == lastIndex)
        {
            index = (index + 1) % clips.Length;
        }

        lastIndex = index;
        return clips[index];
    }
}

[CreateAssetMenu(fileName = "GameAudioLibrary", menuName = "JOSEL/Audio/Game Audio Library")]
public class GameAudioLibrary : ScriptableObject
{
    [Header("Door")]
    public AudioClipSet doorOpen;
    public AudioClipSet doorClose;
    public AudioClipSet heatCheck;

    [Header("Fire")]
    public AudioClip fireAlarmLoop;
    public AudioClipSet fireHurt;
    public AudioClipSet cough;

    [Header("Walking")]
    public AudioClipSet concreteSteps;

    [Header("Outcome")]
    public AudioClip success;
    public AudioClip fail;

    public void AutoResolveMissingClips()
    {
        EnsureSet(ref doorOpen, new[] { "door_open" });
        EnsureSet(ref doorClose, new[] { "door_close" });
        EnsureSet(ref heatCheck, new[] { "heat_check" });

        if (fireAlarmLoop == null)
        {
            fireAlarmLoop = ResolveFirst(new[] { "fire_alarm" });
        }

        EnsureSet(ref fireHurt, new[] { "hurt_fire1.ogg", "hurt_fire2.ogg", "hurt_fire3.ogg", "hurt_fire1", "hurt_fire2", "hurt_fire3" });
        EnsureSet(ref cough, new[] { "cough1", "cough2", "cough3" });
        EnsureSet(ref concreteSteps, new[] { "concrete1", "concrete2" });

        if (success == null)
        {
            success = ResolveFirst(new[] { "escape_success" });
        }

        if (fail == null)
        {
            fail = ResolveFirst(new[] { "escape_fail" });
        }
    }

    private void EnsureSet(ref AudioClipSet set, string[] preferredNames)
    {
        if (set == null)
        {
            set = new AudioClipSet();
        }

        if (set.clips != null && set.clips.Length > 0)
        {
            return;
        }

        set.clips = ResolveMany(preferredNames);
    }

    private static AudioClip ResolveFirst(string[] preferredNames)
    {
        AudioClip[] found = ResolveMany(preferredNames);
        return found.Length > 0 ? found[0] : null;
    }

    private static AudioClip[] ResolveMany(string[] preferredNames)
    {
        AudioClip[] loadedClips = Resources.FindObjectsOfTypeAll<AudioClip>();
#if UNITY_EDITOR
        // In Editor play mode, clips might exist on disk but not be loaded yet.
        // Fall back to AssetDatabase so audio still resolves without manual wiring.
        if (loadedClips == null || loadedClips.Length == 0)
        {
            string[] guids = AssetDatabase.FindAssets("t:AudioClip", new[] { "Assets/JOSEL Audio" });
            loadedClips = new AudioClip[guids.Length];
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                loadedClips[i] = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
            }
        }
#endif

        if (loadedClips == null || loadedClips.Length == 0)
        {
            return Array.Empty<AudioClip>();
        }

        AudioClip[] resolved = new AudioClip[preferredNames.Length];
        int count = 0;

        for (int i = 0; i < preferredNames.Length; i++)
        {
            string expected = preferredNames[i];
            for (int j = 0; j < loadedClips.Length; j++)
            {
                AudioClip clip = loadedClips[j];
                if (clip == null || string.IsNullOrEmpty(clip.name))
                {
                    continue;
                }

                if (clip.name.IndexOf(expected, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    bool exists = false;
                    for (int k = 0; k < count; k++)
                    {
                        if (resolved[k] == clip)
                        {
                            exists = true;
                            break;
                        }
                    }

                    if (!exists)
                    {
                        resolved[count] = clip;
                        count++;
                    }

                    break;
                }
            }
        }

        if (count == 0)
        {
            return Array.Empty<AudioClip>();
        }

        AudioClip[] compact = new AudioClip[count];
        Array.Copy(resolved, compact, count);
        return compact;
    }
}
