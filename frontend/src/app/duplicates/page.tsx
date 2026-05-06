'use client';
import { useState, useEffect } from 'react';
import { useRouter } from 'next/navigation';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import Sidebar from '@/components/Sidebar';
import MediaCard from '@/components/MediaCard';
import MediaViewer from '@/components/MediaViewer';
import { mediaApi, Media } from '@/lib/api';

export default function DuplicatesPage() {
  const router   = useRouter();
  const qc       = useQueryClient();
  const [page,   setPage]   = useState(1);
  const [viewer, setViewer] = useState<number | null>(null);

  useEffect(() => {
    if (typeof window !== 'undefined' && !localStorage.getItem('pv_token'))
      router.replace('/login');
  }, [router]);

  const { data, isLoading } = useQuery({
    queryKey: ['duplicates', page],
    queryFn:  () => mediaApi.duplicates(page, 48).then(r => r.data),
    placeholderData: prev => prev,
  });

  const trashMut = useMutation({
    mutationFn: (id: string) => mediaApi.trash(id),
    onSuccess:  () => {
      qc.invalidateQueries({ queryKey: ['duplicates'] });
      qc.invalidateQueries({ queryKey: ['stats'] });
    },
  });

  const items: Media[] = data?.items ?? [];
  const currentMedia   = viewer !== null ? items[viewer] : null;

  return (
    <div className="flex min-h-screen bg-neutral-950">
      <Sidebar />
      <main className="flex-1 p-6">
        <div className="flex items-center justify-between mb-4">
          <div>
            <h1 className="text-xl font-bold text-white">Duplicates</h1>
            <p className="text-sm text-neutral-400 mt-0.5">
              Photos detected as near-identical by perceptual hash. Keep the best, trash the rest.
            </p>
          </div>
          {data && data.total > 0 && (
            <span className="text-sm text-orange-400 font-medium">
              {data.total} duplicate{data.total !== 1 ? 's' : ''}
            </span>
          )}
        </div>

        {isLoading && <p className="text-neutral-500 text-sm">Scanning for duplicates…</p>}

        {!isLoading && items.length === 0 && (
          <div className="flex flex-col items-center justify-center h-48 text-neutral-500 gap-2">
            <span className="text-3xl">✅</span>
            <span className="text-sm">No duplicates found. Your library is clean!</span>
          </div>
        )}

        <div className="grid grid-cols-3 sm:grid-cols-4 md:grid-cols-5 lg:grid-cols-6
                        xl:grid-cols-7 2xl:grid-cols-8 gap-1">
          {items.map((m, idx) => (
            <div key={m.id} className="relative group">
              <MediaCard media={m} onClick={() => setViewer(idx)} />
              <button
                onClick={() => trashMut.mutate(m.id)}
                className="absolute bottom-1 left-1/2 -translate-x-1/2 px-2 py-0.5
                           bg-red-600/80 text-white text-xs rounded opacity-0
                           group-hover:opacity-100 transition-opacity whitespace-nowrap
                           hover:bg-red-500"
              >
                🗑 Trash
              </button>
            </div>
          ))}
        </div>

        {data && data.totalPages > 1 && (
          <div className="flex items-center justify-center gap-2 py-6 mt-4 border-t border-neutral-800">
            <button disabled={page <= 1} onClick={() => setPage(p => p - 1)}
                    className="px-4 py-2 rounded-lg bg-neutral-800 text-sm text-neutral-300
                               hover:bg-neutral-700 disabled:opacity-30 disabled:cursor-not-allowed">
              Previous
            </button>
            <span className="text-sm text-neutral-400">Page {page} of {data.totalPages}</span>
            <button disabled={page >= (data.totalPages ?? 1)} onClick={() => setPage(p => p + 1)}
                    className="px-4 py-2 rounded-lg bg-neutral-800 text-sm text-neutral-300
                               hover:bg-neutral-700 disabled:opacity-30 disabled:cursor-not-allowed">
              Next
            </button>
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
