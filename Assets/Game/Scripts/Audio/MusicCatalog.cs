using System;
using System.Collections.Generic;
using System.Linq;
using RuinRail.Core;
using UnityEngine;

namespace RuinRail.Audio
{
    [Serializable]
    public sealed class MusicTrackEntry
    {
        public MusicRole Role;
        public AudioClip Clip;
        public bool HasClip => Clip != null;
    }

    [Serializable]
    public sealed class StingerEntry
    {
        public StingerRole Role;
        public AudioClip Clip;
        public bool HasClip => Clip != null;
    }

    [Serializable]
    public sealed class AmbienceEntry
    {
        public Biome Biome;
        public AudioClip Loop;
        [TextArea] public string Character;
        public bool HasClip => Loop != null;
    }

    /// <summary>
    /// The exact V1 music scope as data (art/105): 11 track roles, 6 stinger roles, 3 biome ambience loops. Slots are
    /// fixed by the enums — nothing can add a 12th track; missing clips are reported per role (BLOCKED_EXTERNAL_ASSET).
    /// </summary>
    [CreateAssetMenu(menuName = "RuinRail/Audio/Music Catalog", fileName = "MusicCatalog")]
    public sealed class MusicCatalog : ScriptableObject
    {
        [SerializeField] private List<MusicTrackEntry> _tracks = new();
        [SerializeField] private List<StingerEntry> _stingers = new();
        [SerializeField] private List<AmbienceEntry> _ambience = new();

        public IReadOnlyList<MusicTrackEntry> Tracks => _tracks;
        public IReadOnlyList<StingerEntry> Stingers => _stingers;
        public IReadOnlyList<AmbienceEntry> Ambience => _ambience;

        /// <summary>One slot per role, existing clips preserved.</summary>
        public void EnsureSlots()
        {
            foreach (MusicRole role in Enum.GetValues(typeof(MusicRole))) if (_tracks.All(t => t.Role != role)) _tracks.Add(new MusicTrackEntry { Role = role });
            foreach (StingerRole role in Enum.GetValues(typeof(StingerRole))) if (_stingers.All(s => s.Role != role)) _stingers.Add(new StingerEntry { Role = role });
            foreach (Biome biome in Enum.GetValues(typeof(Biome))) if (_ambience.All(a => a.Biome != biome)) _ambience.Add(new AmbienceEntry { Biome = biome, Character = MusicStateResolver.AmbienceDescription(biome) });
            _tracks = _tracks.GroupBy(t => t.Role).Select(g => g.First()).OrderBy(t => t.Role).ToList();
            _stingers = _stingers.GroupBy(s => s.Role).Select(g => g.First()).OrderBy(s => s.Role).ToList();
            _ambience = _ambience.GroupBy(a => a.Biome).Select(g => g.First()).OrderBy(a => a.Biome).ToList();
        }

        public AudioClip TrackFor(MusicRole role) => _tracks.FirstOrDefault(t => t.Role == role)?.Clip;
        public AudioClip StingerFor(StingerRole role) => _stingers.FirstOrDefault(s => s.Role == role)?.Clip;
        public AudioClip AmbienceFor(Biome biome) => _ambience.FirstOrDefault(a => a.Biome == biome)?.Loop;

        public IEnumerable<MusicRole> MissingTracks => Enum.GetValues(typeof(MusicRole)).Cast<MusicRole>().Where(r => TrackFor(r) == null);
        public IEnumerable<StingerRole> MissingStingers => Enum.GetValues(typeof(StingerRole)).Cast<StingerRole>().Where(r => StingerFor(r) == null);
        public IEnumerable<Biome> MissingAmbience => Enum.GetValues(typeof(Biome)).Cast<Biome>().Where(b => AmbienceFor(b) == null);
        public bool IsContentComplete => !MissingTracks.Any() && !MissingStingers.Any() && !MissingAmbience.Any();
        public bool HasExactSlots => _tracks.Count == MusicStateResolver.TrackCount && _stingers.Count == MusicStateResolver.StingerCount && _ambience.Count == Enum.GetValues(typeof(Biome)).Length;

        public void SetTrack(MusicRole role, AudioClip clip) { EnsureSlots(); _tracks.First(t => t.Role == role).Clip = clip; }
        public void SetStinger(StingerRole role, AudioClip clip) { EnsureSlots(); _stingers.First(s => s.Role == role).Clip = clip; }
        public void SetAmbience(Biome biome, AudioClip clip) { EnsureSlots(); _ambience.First(a => a.Biome == biome).Loop = clip; }
    }
}
