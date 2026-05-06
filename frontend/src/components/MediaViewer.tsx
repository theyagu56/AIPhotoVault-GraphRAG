'use client';
import { useEffect, useCallback, useState, useRef } from 'react';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { Media, Tag, Album, mediaApi, albumApi } from '@/lib/api';

interface Props {
  media: Media;
  onClose: () => void;
  onPrev?: () => void;
  onNext?: () => void;
  onTrashed?: (id: string) => void;
}

export default function MediaViewer({ media, onClose, onPrev, onNext, onTrashed }: Props) {
  const src             = mediaApi.imageUrl(media);
  const [panelOpen, setPanelOpen] = useState(true);
  const qc              = useQueryClient();

  const trashMut = useMutation({
    mutationFn: () => mediaApi.trash(media.id),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['media'] });
      onTrashed?.(media.id);
      onClose();
    },
  });

  // Keyboard nav
  const handleKey = useCallback((e: KeyboardEvent) => {
    if (e.key === 'Escape')      onClose();
    if (e.key === 'ArrowLeft')   onPrev?.();
    if (e.key === 'ArrowRight')  onNext?.();
    if (e.key === 'i')           setPanelOpen(p => !p);
    if (e.key === 'Delete' || e.key === 'Backspace') {
      // only if not focused on an input
      if (document.activeElement?.tagName !== 'INPUT') trashMut.mutate();
    }
  }, [onClose, onPrev, onNext, trashMut]);

  useEffect(() => {
    document.addEventListener('keydown', handleKey);
    document.body.style.overflow = 'hidden';
    return () => {
      document.removeEventListener('keydown', handleKey);
      document.body.style.overflow = '';
    };
  }, [handleKey]);

  return (
    <div className="fixed inset-0 z-50 bg-neutral-950 flex" onClick={onClose}>

      {/* ── Left: media area ──────────────────────────────── */}
      <div
        className="flex-1 flex flex-col min-w-0"
        onClick={e => e.stopPropagation()}
      >
        {/* Top bar */}
        <div className="flex items-center justify-between px-4 py-3 shrink-0
                        bg-neutral-950/80 border-b border-neutral-800/50">
          <div className="flex items-center gap-3">
            <button onClick={onClose}
                    className="text-neutral-400 hover:text-white transition-colors text-lg
                               w-8 h-8 flex items-center justify-center rounded-lg hover:bg-white/10">
              ✕
            </button>
            <span className="text-white text-sm font-medium truncate max-w-xs">{media.fileName}</span>
          </div>

          <div className="flex items-center gap-2">
            {/* Trash button */}
            <button
              onClick={() => {
                if (confirm(`Move "${media.fileName}" to trash?`)) trashMut.mutate();
              }}
              disabled={trashMut.isPending}
              title="Move to Trash (Delete key)"
              className="text-xs px-3 py-1.5 rounded-lg transition-colors border
                         bg-neutral-800 border-neutral-700 text-neutral-400
                         hover:bg-red-600/20 hover:border-red-500/40 hover:text-red-400
                         disabled:opacity-40"
            >
              🗑 Trash
            </button>

            {/* Info panel toggle */}
            <button
              onClick={() => setPanelOpen(p => !p)}
              title="Toggle info panel (i)"
              className={`text-xs px-3 py-1.5 rounded-lg transition-colors border
                         ${panelOpen
                           ? 'bg-blue-600/20 border-blue-500/40 text-blue-400'
                           : 'bg-neutral-800 border-neutral-700 text-neutral-400 hover:text-white'}`}
            >
              ⓘ Info
            </button>
          </div>
        </div>

        {/* Image / Video */}
        <div className="flex-1 flex items-center justify-center relative p-4 overflow-hidden">
          {onPrev && (
            <button
              onClick={e => { e.stopPropagation(); onPrev(); }}
              className="absolute left-3 top-1/2 -translate-y-1/2 text-white/60 hover:text-white
                         text-3xl w-10 h-10 flex items-center justify-center rounded-full
                         hover:bg-white/10 transition-colors z-10"
            >‹</button>
          )}

          {media.mediaType === 'Video' ? (
            <video src={src} controls autoPlay
                   className="max-w-full max-h-full rounded-lg shadow-2xl" />
          ) : (
            <img src={src} alt={media.fileName}
                 className="max-w-full max-h-full object-contain rounded-lg shadow-2xl" />
          )}

          {onNext && (
            <button
              onClick={e => { e.stopPropagation(); onNext(); }}
              className="absolute right-3 top-1/2 -translate-y-1/2 text-white/60 hover:text-white
                         text-3xl w-10 h-10 flex items-center justify-center rounded-full
                         hover:bg-white/10 transition-colors z-10"
            >›</button>
          )}
        </div>
      </div>

      {/* ── Right: info panel ─────────────────────────────── */}
      {panelOpen && (
        <div
          className="w-80 shrink-0 bg-neutral-900 border-l border-neutral-800
                     flex flex-col overflow-hidden"
          onClick={e => e.stopPropagation()}
        >
          <InfoPanel media={media} />
        </div>
      )}
    </div>
  );
}

// ── Info Panel ─────────────────────────────────────────────────
function InfoPanel({ media }: { media: Media }) {
  const qc = useQueryClient();
  const [exifRefreshed, setExifRefreshed] = useState(false);

  const refreshExifMut = useMutation({
    mutationFn: () => mediaApi.refreshExif(media.id),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['media'] });
      setExifRefreshed(true);
    },
  });

  const { data: tagData } = useQuery({
    queryKey: ['media-tags', media.id],
    queryFn:  () => mediaApi.getTags(media.id).then(r => r.data),
  });

  const { data: allAlbums } = useQuery({
    queryKey: ['albums'],
    queryFn:  () => albumApi.list().then(r => r.data),
  });

  const { data: mediaAlbums, refetch: refetchMediaAlbums } = useQuery({
    queryKey: ['media-albums', media.id],
    queryFn:  () => mediaApi.getAlbums(media.id).then(r => r.data),
  });

  return (
    <div className="flex-1 overflow-y-auto">
      {/* Caption */}
      {tagData?.caption && (
        <div className="px-4 pt-4 pb-3 border-b border-neutral-800">
          <p className="text-xs text-purple-400 font-semibold uppercase tracking-wide mb-1.5">
            ✦ AI Caption
          </p>
          <p className="text-sm text-neutral-200 leading-relaxed italic">
            "{tagData.caption}"
          </p>
        </div>
      )}

      {/* Metadata */}
      <div className="px-4 py-3 border-b border-neutral-800 space-y-2">
        <div className="flex items-center justify-between mb-2">
          <p className="text-xs text-neutral-500 font-semibold uppercase tracking-wide">Details</p>
          <button
            onClick={() => refreshExifMut.mutate()}
            disabled={refreshExifMut.isPending}
            title="Re-extract EXIF metadata from file"
            className="text-xs text-neutral-600 hover:text-blue-400 transition-colors disabled:opacity-40"
          >
            {refreshExifMut.isPending ? '…' : exifRefreshed ? '✓ Refreshed' : '↻ Refresh EXIF'}
          </button>
        </div>
        <MetaRow icon="📄" label="Name"       value={media.fileName} />
        {media.capturedAt && (
          <MetaRow icon="📅" label="Date"
            value={new Date(media.capturedAt).toLocaleDateString('en-US', {
              weekday: 'short', year: 'numeric', month: 'long', day: 'numeric',
              hour: '2-digit', minute: '2-digit'
            })}
          />
        )}
        {(media.width && media.height) && (
          <MetaRow icon="📐" label="Resolution" value={`${media.width} × ${media.height} px`} />
        )}
        {media.fileSizeBytes != null && media.fileSizeBytes > 0 && (
          <MetaRow icon="💾" label="Size"       value={formatBytes(media.fileSizeBytes)} />
        )}
        {media.mediaType === 'Video' && media.durationSeconds != null && (
          <MetaRow icon="⏱" label="Duration"
            value={formatDuration(media.durationSeconds)}
          />
        )}
        {media.cameraModel && (
          <MetaRow icon="📷" label="Camera"     value={media.cameraModel} />
        )}
        {media.mimeType && (
          <MetaRow icon="🎞️" label="Format"     value={media.mimeType} />
        )}
        {media.isBlurry && (
          <MetaRow icon="🌫️" label="Quality"
            value={`Blurry (score: ${media.blurScore?.toFixed(0) ?? '—'})`}
            valueClass="text-yellow-400"
          />
        )}
        {media.isDuplicate && (
          <MetaRow icon="🔁" label="Note"
            value="Near-duplicate detected"
            valueClass="text-orange-400"
          />
        )}
        {media.aiProcessed && (
          <MetaRow icon="✦"  label="AI"         value={`Tagged by ${media.aiModelUsed ?? 'AI'}`}
            valueClass="text-purple-400"
          />
        )}
      </div>

      {/* Location */}
      {(media.latitude != null && media.longitude != null) && (
        <div className="px-4 py-3 border-b border-neutral-800">
          <p className="text-xs text-neutral-500 font-semibold uppercase tracking-wide mb-2">Location</p>
          <MetaRow icon="📍" label="GPS"
            value={`${media.latitude.toFixed(5)}, ${media.longitude.toFixed(5)}`}
          />
          <a
            href={`https://maps.google.com/?q=${media.latitude},${media.longitude}`}
            target="_blank"
            rel="noopener noreferrer"
            className="mt-2 inline-flex items-center gap-1.5 text-xs text-blue-400
                       hover:text-blue-300 transition-colors"
          >
            Open in Google Maps ↗
          </a>
        </div>
      )}

      {/* Tags */}
      <div className="px-4 py-3 border-b border-neutral-800">
        <p className="text-xs text-neutral-500 font-semibold uppercase tracking-wide mb-2">Tags</p>
        {(!tagData?.tags || tagData.tags.length === 0) && (
          <p className="text-xs text-neutral-600 italic">
            {media.aiProcessed ? 'No tags generated' : 'Not yet processed'}
          </p>
        )}
        <div className="flex flex-wrap gap-1.5">
          {tagData?.tags?.map((tag: Tag) => (
            <span key={tag.id}
                  title={`${tag.category} · confidence: ${((tag.confidence ?? 1) * 100).toFixed(0)}%`}
                  className={`px-2 py-0.5 rounded-full text-xs font-medium
                    ${tag.source === 'AI'
                      ? 'bg-purple-600/20 text-purple-300 border border-purple-500/30'
                      : 'bg-neutral-700 text-neutral-300 border border-neutral-600'}`}>
              {tag.name}
            </span>
          ))}
        </div>
      </div>

      {/* Similar Photos */}
      <SimilarPhotosStrip mediaId={media.id} />

      {/* Albums — current membership + add picker */}
      <div className="px-4 py-3">
        <p className="text-xs text-neutral-500 font-semibold uppercase tracking-wide mb-2">Albums</p>

        {/* Albums this photo already belongs to */}
        {mediaAlbums && mediaAlbums.length > 0 && (
          <div className="flex flex-wrap gap-1.5 mb-3">
            {mediaAlbums.map(album => (
              <AlbumChip
                key={album.id}
                album={album}
                mediaId={media.id}
                onRemoved={() => refetchMediaAlbums()}
              />
            ))}
          </div>
        )}

        <AlbumPicker
          mediaId={media.id}
          allAlbums={allAlbums ?? []}
          memberAlbumIds={(mediaAlbums ?? []).map(a => a.id)}
          onAdded={() => refetchMediaAlbums()}
        />
      </div>
    </div>
  );
}

// ── Album chip with remove button ──────────────────────────────
function AlbumChip({ album, mediaId, onRemoved }: {
  album: Album; mediaId: string; onRemoved: () => void;
}) {
  const qc = useQueryClient();
  const removeMut = useMutation({
    mutationFn: () => albumApi.removeMedia(album.id, mediaId),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['albums'] });
      onRemoved();
    },
  });

  return (
    <span className="inline-flex items-center gap-1 px-2 py-0.5 rounded-full text-xs
                     bg-blue-600/20 text-blue-300 border border-blue-500/30">
      {album.name}
      <button
        onClick={() => removeMut.mutate()}
        disabled={removeMut.isPending}
        className="ml-0.5 text-blue-400 hover:text-red-400 transition-colors disabled:opacity-40
                   leading-none"
        title={`Remove from "${album.name}"`}
      >×</button>
    </span>
  );
}

// ── Album Picker with autocomplete ─────────────────────────────
function AlbumPicker({ mediaId, allAlbums, memberAlbumIds, onAdded }: {
  mediaId: string;
  allAlbums: Album[];
  memberAlbumIds: string[];
  onAdded: () => void;
}) {
  const qc              = useQueryClient();
  const [query, setQuery]   = useState('');
  const [open,  setOpen]    = useState(false);
  const [justAdded, setJustAdded] = useState<string[]>([]);
  const inputRef            = useRef<HTMLInputElement>(null);

  const filtered = allAlbums.filter(a =>
    a.name.toLowerCase().includes(query.toLowerCase()) &&
    a.albumType === 'User' &&
    !memberAlbumIds.includes(a.id) &&
    !justAdded.includes(a.id)
  );

  const addMut = useMutation({
    mutationFn: (albumId: string) => albumApi.addMedia(albumId, mediaId),
    onSuccess: (_, albumId) => {
      setJustAdded(prev => [...prev, albumId]);
      qc.invalidateQueries({ queryKey: ['albums'] });
      setQuery('');
      setOpen(false);
      onAdded();
    },
  });

  return (
    <div className="relative">
      <input
        ref={inputRef}
        type="text"
        value={query}
        placeholder="Add to album…"
        onChange={e => { setQuery(e.target.value); setOpen(true); }}
        onFocus={() => setOpen(true)}
        onBlur={() => setTimeout(() => setOpen(false), 150)}
        className="w-full bg-neutral-800 border border-neutral-700 rounded-lg px-3 py-2
                   text-sm text-white placeholder-neutral-500 focus:outline-none
                   focus:border-blue-500 transition-colors"
      />

      {open && filtered.length > 0 && (
        <div className="absolute top-full mt-1 left-0 right-0 bg-neutral-800 border
                        border-neutral-700 rounded-lg overflow-hidden shadow-xl z-20
                        max-h-48 overflow-y-auto">
          {filtered.map(album => (
            <button
              key={album.id}
              onMouseDown={() => addMut.mutate(album.id)}
              className="w-full text-left px-3 py-2 text-sm text-neutral-200
                         hover:bg-neutral-700 transition-colors flex items-center justify-between"
            >
              <span className="truncate">{album.name}</span>
              <span className="text-neutral-500 text-xs shrink-0 ml-2">+ Add</span>
            </button>
          ))}
        </div>
      )}

      {open && query.length > 0 && filtered.length === 0 && (
        <div className="absolute top-full mt-1 left-0 right-0 bg-neutral-800 border
                        border-neutral-700 rounded-lg px-3 py-2 text-xs text-neutral-500 z-20">
          No matching albums
        </div>
      )}
    </div>
  );
}

// ── Similar Photos horizontal strip ────────────────────────────
function SimilarPhotosStrip({ mediaId }: { mediaId: string }) {
  const { data, isLoading } = useQuery({
    queryKey: ['similar', mediaId],
    queryFn:  () => mediaApi.similar(mediaId, 12).then(r => r.data),
    staleTime: 60_000,
  });

  const similar = data?.items ?? [];

  if (isLoading) return (
    <div className="px-4 py-3 border-b border-neutral-800">
      <p className="text-xs text-neutral-500 font-semibold uppercase tracking-wide mb-2">
        Similar Photos
      </p>
      <div className="flex gap-1.5 overflow-x-auto pb-1">
        {[...Array(6)].map((_, i) => (
          <div key={i} className="w-16 h-16 shrink-0 rounded-lg bg-neutral-800 animate-pulse" />
        ))}
      </div>
    </div>
  );

  if (!similar.length) return null;

  return (
    <div className="px-4 py-3 border-b border-neutral-800">
      <p className="text-xs text-neutral-500 font-semibold uppercase tracking-wide mb-2">
        Similar Photos
        <span className="ml-1.5 text-purple-500 font-normal normal-case">via graph</span>
      </p>
      <div className="flex gap-1.5 overflow-x-auto pb-1 scrollbar-thin">
        {similar.map(m => (
          <a
            key={m.id}
            href={`${mediaApi.imageUrl(m)}`}
            target="_blank"
            rel="noopener noreferrer"
            title={m.fileName}
            className="shrink-0 w-16 h-16 rounded-lg overflow-hidden border border-neutral-700
                       hover:border-purple-500 transition-colors"
          >
            <img
              src={mediaApi.thumbnailUrl(m.id, 'sm')}
              alt={m.fileName}
              className="w-full h-full object-cover"
              loading="lazy"
            />
          </a>
        ))}
      </div>
    </div>
  );
}

// ── Small helpers ───────────────────────────────────────────────
function MetaRow({ icon, label, value, valueClass = 'text-neutral-200' }: {
  icon: string; label: string; value: string; valueClass?: string;
}) {
  return (
    <div className="flex items-start gap-2 text-xs">
      <span className="shrink-0 w-4 text-center opacity-60">{icon}</span>
      <span className="text-neutral-500 shrink-0 w-16">{label}</span>
      <span className={`${valueClass} break-words min-w-0`}>{value}</span>
    </div>
  );
}

function formatDuration(seconds: number) {
  const h = Math.floor(seconds / 3600);
  const m = Math.floor((seconds % 3600) / 60);
  const s = Math.floor(seconds % 60);
  return h > 0
    ? `${h}:${m.toString().padStart(2, '0')}:${s.toString().padStart(2, '0')}`
    : `${m}:${s.toString().padStart(2, '0')}`;
}

function formatBytes(bytes: number): string {
  if (bytes < 1024)        return `${bytes} B`;
  if (bytes < 1024 ** 2)   return `${(bytes / 1024).toFixed(1)} KB`;
  if (bytes < 1024 ** 3)   return `${(bytes / 1024 ** 2).toFixed(1)} MB`;
  return `${(bytes / 1024 ** 3).toFixed(2)} GB`;
}
