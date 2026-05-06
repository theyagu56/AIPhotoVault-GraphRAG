'use client';
import { Media, mediaApi } from '@/lib/api';

interface Props {
  media: Media;
  onClick: (media: Media) => void;
  selected?: boolean;
}

export default function MediaCard({ media, onClick, selected }: Props) {
  const thumb = mediaApi.thumbnailUrl(media.id, 'md');

  return (
    <div
      onClick={() => onClick(media)}
      className={`relative cursor-pointer overflow-hidden rounded-lg bg-neutral-800
                  group transition-all duration-150
                  ${selected ? 'ring-2 ring-blue-500' : 'hover:ring-1 hover:ring-white/20'}`}
    >
      {/* Thumbnail */}
      <div className="aspect-square relative">
        <img
          src={thumb}
          alt={media.fileName}
          className="w-full h-full object-cover transition-transform duration-200 group-hover:scale-105"
          loading="lazy"
          onError={(e) => {
            (e.target as HTMLImageElement).style.display = 'none';
          }}
        />

        {/* Video badge */}
        {media.mediaType === 'Video' && (
          <div className="absolute bottom-1.5 right-1.5 bg-black/70 rounded px-1.5 py-0.5
                          text-xs text-white flex items-center gap-1">
            ▶
            {media.durationSeconds != null && (
              <span>{formatDuration(media.durationSeconds)}</span>
            )}
          </div>
        )}

        {/* Blur badge */}
        {media.isBlurry && (
          <div className="absolute top-1.5 left-1.5 bg-yellow-500/80 rounded-full px-1.5 py-0.5
                          text-[9px] font-bold text-black" title="Blurry photo">
            blur
          </div>
        )}

        {/* Duplicate badge */}
        {media.isDuplicate && (
          <div className="absolute top-1.5 right-1.5 bg-orange-500/80 rounded-full px-1.5 py-0.5
                          text-[9px] font-bold text-white" title="Duplicate detected">
            dup
          </div>
        )}

        {/* AI processed badge */}
        {media.aiProcessed && !media.isBlurry && !media.isDuplicate && (
          <div className="absolute top-1.5 left-1.5 bg-purple-600/80 rounded-full w-4 h-4
                          flex items-center justify-center text-[9px] text-white" title="AI tagged">
            ✦
          </div>
        )}

        {/* Hover overlay */}
        <div className="absolute inset-0 bg-black/0 group-hover:bg-black/10 transition-colors" />

        {/* Filename on hover */}
        <div className="absolute bottom-0 left-0 right-0 bg-gradient-to-t from-black/60 to-transparent
                        px-1.5 py-1.5 opacity-0 group-hover:opacity-100 transition-opacity">
          <p className="text-white text-[10px] truncate leading-tight">{media.fileName}</p>
          {media.capturedAt && (
            <p className="text-white/60 text-[9px]">
              {new Date(media.capturedAt).toLocaleDateString()}
            </p>
          )}
        </div>
      </div>
    </div>
  );
}

function formatDuration(seconds: number) {
  const m = Math.floor(seconds / 60);
  const s = Math.floor(seconds % 60);
  return `${m}:${s.toString().padStart(2, '0')}`;
}
