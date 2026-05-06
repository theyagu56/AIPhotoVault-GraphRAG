'use client';
import { useState, useEffect } from 'react';
import { useRouter, useParams } from 'next/navigation';
import Link from 'next/link';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import Sidebar from '@/components/Sidebar';
import MediaCard from '@/components/MediaCard';
import MediaViewer from '@/components/MediaViewer';
import { albumApi, mediaApi, Media } from '@/lib/api';

export default function AlbumDetailPage() {
  const { id }  = useParams<{ id: string }>();
  const router  = useRouter();
  const qc      = useQueryClient();
  const [viewer, setViewer] = useState<number | null>(null);

  useEffect(() => {
    if (typeof window !== 'undefined' && !localStorage.getItem('pv_token')) {
      router.replace('/login');
    }
  }, [router]);

  const { data: album, isLoading } = useQuery({
    queryKey: ['album', id],
    queryFn:  () => albumApi.getById(id).then(r => r.data),
    enabled:  !!id,
  });

  const deleteMut = useMutation({
    mutationFn: () => albumApi.delete(id),
    onSuccess: () => router.push('/albums'),
  });

  const removeMedia = useMutation({
    mutationFn: (mediaId: string) => albumApi.removeMedia(id, mediaId),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['album', id] }),
  });

  const items: Media[] = album?.media ?? [];
  const currentMedia   = viewer !== null ? items[viewer] : null;

  return (
    <div className="flex min-h-screen bg-neutral-950">
      <Sidebar />
      <main className="flex-1 p-6">
        {/* Header */}
        <div className="flex items-center gap-4 mb-6">
          <Link href="/albums" className="text-neutral-400 hover:text-white text-sm">
            ← Albums
          </Link>
          <h1 className="text-xl font-bold text-white flex-1">
            {isLoading ? '…' : album?.name}
          </h1>
          {album && (
            <button
              onClick={() => {
                if (confirm(`Delete album "${album.name}"? Photos won't be deleted.`)) {
                  deleteMut.mutate();
                }
              }}
              className="px-3 py-1.5 text-xs text-red-400 hover:text-red-300 border
                         border-red-400/30 hover:border-red-400/60 rounded-lg transition-colors"
            >
              Delete Album
            </button>
          )}
        </div>

        {!isLoading && items.length === 0 && (
          <div className="flex flex-col items-center justify-center h-48 text-neutral-500 gap-2">
            <span className="text-3xl">📷</span>
            <span className="text-sm">This album is empty.</span>
          </div>
        )}

        {items.length > 0 && (
          <div className="grid grid-cols-3 sm:grid-cols-4 md:grid-cols-5 lg:grid-cols-6
                          xl:grid-cols-7 2xl:grid-cols-8 gap-1">
            {items.map((m, idx) => (
              <div key={m.id} className="relative group">
                <MediaCard media={m} onClick={() => setViewer(idx)} />
                <button
                  onClick={() => removeMedia.mutate(m.id)}
                  className="absolute top-1 right-1 w-5 h-5 rounded-full bg-black/70
                             text-white text-xs opacity-0 group-hover:opacity-100
                             transition-opacity flex items-center justify-center
                             hover:bg-red-600"
                  title="Remove from album"
                >
                  ✕
                </button>
              </div>
            ))}
          </div>
        )}
      </main>

      {currentMedia && (
        <MediaViewer
          media={currentMedia}
          onClose={() => setViewer(null)}
          onPrev={viewer! > 0 ? () => setViewer(v => v! - 1) : undefined}
          onNext={viewer! < items.length - 1 ? () => setViewer(v => v! + 1) : undefined}
        />
      )}
    </div>
  );
}
