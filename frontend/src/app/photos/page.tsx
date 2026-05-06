'use client';
import { useState, useEffect, useRef } from 'react';
import { useRouter } from 'next/navigation';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import Sidebar from '@/components/Sidebar';
import MediaCard from '@/components/MediaCard';
import MediaViewer from '@/components/MediaViewer';
import { mediaApi, Media } from '@/lib/api';

const PAGE_SIZE = 48;

export default function PhotosPage() {
  const router      = useRouter();
  const queryClient = useQueryClient();

  const [page,      setPage]     = useState(1);
  const [search,    setSearch]   = useState('');
  const [filter,    setFilter]   = useState<'all' | 'Photo' | 'Video'>('all');
  const [viewer,    setViewer]   = useState<number | null>(null);
  const [scanPath,  setScanPath] = useState('/Volumes/LaCie/ONE PHOTOS/');
  const [graphMode, setGraphMode] = useState(false); // 🔗 semantic graph search toggle

  // Auth guard
  useEffect(() => {
    if (typeof window !== 'undefined' && !localStorage.getItem('pv_token')) {
      router.replace('/login');
    }
  }, [router]);

  // ── Standard media list ───────────────────────────────────────
  const standardQuery = useQuery({
    queryKey: ['media', page, search, filter],
    queryFn: () => mediaApi.list({
      page,
      pageSize:  PAGE_SIZE,
      search:    search || undefined,
      mediaType: filter !== 'all' ? filter : undefined,
      inTrash:   false,
      sortBy:    'capturedAt',
      desc:      true,
    }).then(r => r.data),
    placeholderData: prev => prev,
    enabled: !graphMode || !search,
  });

  // ── Graph (semantic) search ───────────────────────────────────
  const graphQuery = useQuery({
    queryKey: ['graph-search', search, page],
    queryFn: () => mediaApi.graphSearch({ q: search, maxHops: 2, page, pageSize: PAGE_SIZE })
                           .then(r => r.data),
    placeholderData: prev => prev,
    enabled: graphMode && search.length > 0,
  });

  // Pick the active data source
  const activeData     = graphMode && search ? graphQuery.data  : standardQuery.data;
  const isLoading      = graphMode && search ? graphQuery.isLoading : standardQuery.isLoading;
  const isError        = graphMode && search ? graphQuery.isError   : standardQuery.isError;
  const items: Media[] = activeData?.items ?? [];
  const expandedTags: string[] = graphMode && search && graphQuery.data
    ? graphQuery.data.expandedTags
    : [];

  const scanMutation = useMutation({
    mutationFn: () => mediaApi.scan(scanPath).then(r => r.data),
    onSuccess: () => {
      setPage(1);
      queryClient.invalidateQueries({ queryKey: ['media'] });
      queryClient.invalidateQueries({ queryKey: ['stats'] });
    },
  });

  // Debounced search input
  const searchTimer = useRef<ReturnType<typeof setTimeout> | undefined>(undefined);
  function handleSearchChange(val: string) {
    clearTimeout(searchTimer.current);
    searchTimer.current = setTimeout(() => {
      setSearch(val);
      setPage(1);
    }, 400);
  }

  function toggleGraphMode() {
    setGraphMode(g => !g);
    setPage(1);
  }

  function openViewer(media: Media) {
    const idx = items.indexOf(media);
    if (idx !== -1) setViewer(idx);
  }

  const currentMedia = viewer !== null ? items[viewer] : null;

  return (
    <div className="flex min-h-screen bg-neutral-950">
      <Sidebar />

      <main className="flex-1 flex flex-col min-w-0">
        {/* ── Top bar ───────────────────────────────────────────── */}
        <div className="sticky top-0 z-10 bg-neutral-950/90 backdrop-blur border-b border-neutral-800 px-6 py-3">
          <div className="flex flex-wrap items-center gap-3">

            {/* Scan path */}
            <input
              type="text"
              value={scanPath}
              onChange={e => setScanPath(e.target.value)}
              className="min-w-72 flex-1 bg-neutral-800 border border-neutral-700 rounded-lg
                         px-4 py-2 text-sm text-white placeholder-neutral-500
                         focus:outline-none focus:border-blue-500"
              aria-label="Photo folder path"
            />
            <button
              onClick={() => scanMutation.mutate()}
              disabled={scanMutation.isPending || !scanPath.trim()}
              className="px-4 py-2 rounded-lg bg-emerald-600 text-sm font-medium text-white
                         hover:bg-emerald-500 disabled:opacity-50 disabled:cursor-not-allowed"
            >
              {scanMutation.isPending ? 'Scanning...' : 'Scan path'}
            </button>

            {/* Search input + Graph mode toggle (joined pill) */}
            <div className="flex items-center">
              <input
                type="search"
                placeholder={graphMode ? 'Semantic search…' : 'Search photos…'}
                onChange={e => handleSearchChange(e.target.value)}
                className={`w-52 bg-neutral-800 border-y border-l rounded-l-lg px-4 py-2 text-sm
                            text-white placeholder-neutral-500 focus:outline-none transition-colors
                            ${graphMode
                              ? 'border-purple-500 focus:border-purple-400'
                              : 'border-neutral-700 focus:border-blue-500'}`}
              />
              <button
                onClick={toggleGraphMode}
                title={graphMode
                  ? 'Graph search ON — click to use keyword search'
                  : 'Switch to semantic graph search (finds photos by meaning)'}
                className={`px-3 py-2 rounded-r-lg border text-xs font-semibold transition-colors shrink-0
                            ${graphMode
                              ? 'bg-purple-600 border-purple-500 text-white'
                              : 'bg-neutral-800 border-neutral-700 text-neutral-500 hover:text-purple-400 hover:border-purple-600/50'}`}
              >
                🔗 Graph
              </button>
            </div>

            {/* Media type filters — hidden in graph mode */}
            {!graphMode && (
              <div className="flex gap-1">
                {(['all', 'Photo', 'Video'] as const).map(f => (
                  <button
                    key={f}
                    onClick={() => { setFilter(f); setPage(1); }}
                    className={`px-3 py-1.5 rounded-lg text-xs font-medium transition-colors
                      ${filter === f
                        ? 'bg-blue-600 text-white'
                        : 'bg-neutral-800 text-neutral-400 hover:text-white'}`}
                  >
                    {f === 'all' ? 'All' : f + 's'}
                  </button>
                ))}
              </div>
            )}

            {activeData && (
              <span className="text-xs text-neutral-500 ml-auto">
                {activeData.total.toLocaleString()} items
              </span>
            )}
          </div>

          {/* Graph mode: expanded tag chips (what the graph also searched) */}
          {graphMode && search && expandedTags.length > 0 && (
            <div className="mt-2 flex flex-wrap items-center gap-1.5">
              <span className="text-xs text-neutral-500 shrink-0">Also matched via graph:</span>
              {expandedTags.map(tag => (
                <span key={tag}
                      className="px-2 py-0.5 rounded-full text-xs bg-purple-600/20
                                 text-purple-300 border border-purple-500/30">
                  {tag}
                </span>
              ))}
            </div>
          )}

          {/* Graph mode idle hint */}
          {graphMode && !search && (
            <p className="mt-2 text-xs text-purple-400/60">
              🔗 Semantic search active — type a concept like "beach", "birthday", or "sunset" to search by meaning.
            </p>
          )}

          {scanMutation.data && (
            <div className="mt-2 text-xs text-neutral-400">
              Scanned {scanMutation.data.discovered.toLocaleString()} files · imported{' '}
              {scanMutation.data.imported.toLocaleString()} · skipped{' '}
              {scanMutation.data.skipped.toLocaleString()} · failed {scanMutation.data.failed.toLocaleString()}.
            </div>
          )}
          {scanMutation.isError && (
            <div className="mt-2 text-xs text-red-400">
              Could not scan that folder. Check that the path exists and the API has read permission.
            </div>
          )}
        </div>

        {/* ── Content grid ──────────────────────────────────────── */}
        <div className="flex-1 p-4">
          {isLoading && items.length === 0 && (
            <div className="flex items-center justify-center h-64 text-neutral-500">Loading…</div>
          )}
          {isError && (
            <div className="flex items-center justify-center h-64 text-red-400 text-sm">
              Could not load photos — is the API running on port 5050?
            </div>
          )}
          {!isLoading && !isError && items.length === 0 && (
            <div className="flex flex-col items-center justify-center h-64 text-neutral-500 gap-2">
              {graphMode && search
                ? <>
                    <span className="text-3xl">🔗</span>
                    <span className="text-sm">No photos matched "{search}" in the graph.</span>
                    <span className="text-xs text-neutral-600">
                      Run AI Processing + Build Graph Index in Admin first.
                    </span>
                  </>
                : <>
                    <span className="text-4xl">📷</span>
                    <span className="text-sm">No photos yet. Scan a folder to get started.</span>
                  </>
              }
            </div>
          )}
          {items.length > 0 && (
            <div className="grid grid-cols-3 sm:grid-cols-4 md:grid-cols-5 lg:grid-cols-6
                            xl:grid-cols-7 2xl:grid-cols-8 gap-1">
              {items.map(m => (
                <MediaCard key={m.id} media={m} onClick={openViewer} />
              ))}
            </div>
          )}
        </div>

        {/* ── Pagination ────────────────────────────────────────── */}
        {activeData && activeData.totalPages > 1 && (
          <div className="flex items-center justify-center gap-2 py-6 border-t border-neutral-800">
            <button
              disabled={page <= 1}
              onClick={() => setPage(p => p - 1)}
              className="px-4 py-2 rounded-lg bg-neutral-800 text-sm text-neutral-300
                         hover:bg-neutral-700 disabled:opacity-30 disabled:cursor-not-allowed"
            >
              Previous
            </button>
            <span className="text-sm text-neutral-400">
              Page {page} of {activeData.totalPages}
            </span>
            <button
              disabled={page >= activeData.totalPages}
              onClick={() => setPage(p => p + 1)}
              className="px-4 py-2 rounded-lg bg-neutral-800 text-sm text-neutral-300
                         hover:bg-neutral-700 disabled:opacity-30 disabled:cursor-not-allowed"
            >
              Next
            </button>
          </div>
        )}
      </main>

      {/* ── Media viewer lightbox ─────────────────────────────── */}
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
