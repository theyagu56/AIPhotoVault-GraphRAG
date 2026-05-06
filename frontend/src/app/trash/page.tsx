'use client';
import { useState, useEffect } from 'react';
import { useRouter } from 'next/navigation';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import Sidebar from '@/components/Sidebar';
import MediaCard from '@/components/MediaCard';
import MediaViewer from '@/components/MediaViewer';
import { mediaApi, Media } from '@/lib/api';

const PAGE_SIZE = 48;

export default function TrashPage() {
  const router   = useRouter();
  const qc       = useQueryClient();
  const [page,   setPage]   = useState(1);
  const [viewer, setViewer] = useState<number | null>(null);

  useEffect(() => {
    if (typeof window !== 'undefined' && !localStorage.getItem('pv_token')) {
      router.replace('/login');
    }
  }, [router]);

  const { data, isLoading } = useQuery({
    queryKey: ['trash', page],
    queryFn: () => mediaApi.list({ page, pageSize: PAGE_SIZE, inTrash: true }).then(r => r.data),
    placeholderData: prev => prev,
  });

  const restoreMut = useMutation({
    mutationFn: (id: string) => mediaApi.restore(id),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['trash'] });
      qc.invalidateQueries({ queryKey: ['media'] });
      qc.invalidateQueries({ queryKey: ['stats'] });
    },
  });

  const items: Media[] = data?.items ?? [];
  const currentMedia   = viewer !== null ? items[viewer] : null;

  return (
    <div className="flex min-h-screen bg-neutral-950">
      <Sidebar />
      <main className="flex-1 p-6">
        <div className="flex items-center justify-between mb-6">
          <h1 className="text-xl font-bold text-white">Trash</h1>
          {data && data.total > 0 && (
            <span className="text-sm text-neutral-400">{data.total} item(s)</span>
          )}
        </div>

        {isLoading && (
          <div className="text-neutral-500 text-sm">Loading…</div>
        )}

        {!isLoading && items.length === 0 && (
          <div className="flex flex-col items-center justify-center h-48 text-neutral-500 gap-2">
            <span className="text-3xl">🗑️</span>
            <span className="text-sm">Trash is empty.</span>
          </div>
        )}

        <div className="grid grid-cols-3 sm:grid-cols-4 md:grid-cols-5 lg:grid-cols-6
                        xl:grid-cols-7 2xl:grid-cols-8 gap-1">
          {items.map((m, idx) => (
            <div key={m.id} className="relative group">
              <div className="opacity-60 hover:opacity-80 transition-opacity">
                <MediaCard media={m} onClick={() => setViewer(idx)} />
              </div>
              <button
                onClick={() => restoreMut.mutate(m.id)}
                className="absolute bottom-1 left-1/2 -translate-x-1/2 px-2 py-0.5
                           bg-green-600/80 text-white text-xs rounded opacity-0
                           group-hover:opacity-100 transition-opacity whitespace-nowrap
                           hover:bg-green-500"
              >
                Restore
              </button>
            </div>
          ))}
        </div>

        {data && data.totalPages > 1 && (
          <div className="flex items-center justify-center gap-2 py-6 border-t border-neutral-800 mt-6">
            <button disabled={page <= 1} onClick={() => setPage(p => p - 1)}
                    className="px-4 py-2 rounded-lg bg-neutral-800 text-sm text-neutral-300
                               hover:bg-neutral-700 disabled:opacity-30 disabled:cursor-not-allowed">
              Previous
            </button>
            <span className="text-sm text-neutral-400">Page {page} of {data.totalPages}</span>
            <button disabled={page >= data.totalPages} onClick={() => setPage(p => p + 1)}
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
