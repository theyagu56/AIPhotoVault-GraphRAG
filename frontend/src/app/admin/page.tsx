'use client';
import { useEffect, useState } from 'react';
import { useRouter } from 'next/navigation';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import Sidebar from '@/components/Sidebar';
import { userApi, authApi, mediaApi } from '@/lib/api';

export default function AdminPage() {
  const router = useRouter();
  const qc     = useQueryClient();

  const { data: me } = useQuery({
    queryKey: ['me'],
    queryFn:  () => authApi.me().then(r => r.data),
    retry:    false,
  });

  useEffect(() => {
    if (typeof window !== 'undefined' && !localStorage.getItem('pv_token')) {
      router.replace('/login');
    }
  }, [router]);

  const { data: pending, isLoading: loadingPending } = useQuery({
    queryKey: ['users', 'pending'],
    queryFn:  () => userApi.pending().then(r => r.data),
    enabled:  me?.role === 'Admin',
  });

  const { data: allUsers, isLoading: loadingAll } = useQuery({
    queryKey: ['users', 'all'],
    queryFn:  () => userApi.all().then(r => r.data),
    enabled:  me?.role === 'Admin',
  });

  const approveMut = useMutation({
    mutationFn: (id: string) => userApi.approve(id),
    onSuccess:  () => {
      qc.invalidateQueries({ queryKey: ['users'] });
    },
  });

  const rejectMut = useMutation({
    mutationFn: (id: string) => userApi.reject(id),
    onSuccess:  () => {
      qc.invalidateQueries({ queryKey: ['users'] });
    },
  });

  if (me && me.role !== 'Admin') {
    return (
      <div className="flex min-h-screen bg-neutral-950">
        <Sidebar />
        <main className="flex-1 flex items-center justify-center">
          <div className="text-center text-neutral-500">
            <div className="text-4xl mb-2">🔒</div>
            <p className="text-sm">Admin access required.</p>
          </div>
        </main>
      </div>
    );
  }

  return (
    <div className="flex min-h-screen bg-neutral-950">
      <Sidebar />
      <main className="flex-1 p-6 max-w-3xl">
        <h1 className="text-xl font-bold text-white mb-6">Admin Panel</h1>

        {/* GraphRAG */}
        <section className="mb-8">
          <h2 className="text-sm font-semibold text-neutral-300 uppercase tracking-wide mb-3">
            Knowledge Graph (GraphRAG)
          </h2>
          <div className="bg-neutral-900 border border-neutral-800 rounded-xl px-4 py-4 space-y-3">
            <p className="text-sm text-neutral-400">
              Builds a local knowledge graph — Photo → Tag, Location, Event nodes with weighted edges.
              Run after AI tagging to enable future semantic search. 100% local, no data sent externally.
            </p>
            <GraphSection />
          </div>
        </section>

        {/* AI Processing */}
        <section className="mb-8">
          <h2 className="text-sm font-semibold text-neutral-300 uppercase tracking-wide mb-3">
            AI Processing
          </h2>
          <div className="bg-neutral-900 border border-neutral-800 rounded-xl px-4 py-4 space-y-3">
            <p className="text-sm text-neutral-400">
              Queue all unprocessed photos for blur detection, duplicate fingerprinting, and GPT-4o tagging.
              Run this once after adding new photos or after the initial setup.
            </p>
            <ReprocessButton />
          </div>
        </section>

        {/* Pending approvals */}
        <section className="mb-8">
          <h2 className="text-sm font-semibold text-neutral-300 uppercase tracking-wide mb-3">
            Pending Approvals
            {pending && pending.length > 0 && (
              <span className="ml-2 bg-amber-500 text-black text-xs font-bold px-1.5 py-0.5 rounded-full">
                {pending.length}
              </span>
            )}
          </h2>

          {loadingPending && <p className="text-neutral-500 text-sm">Loading…</p>}

          {!loadingPending && pending?.length === 0 && (
            <p className="text-neutral-500 text-sm">No pending approvals.</p>
          )}

          <div className="space-y-2">
            {pending?.map(user => (
              <div key={user.id}
                   className="flex items-center gap-4 bg-neutral-900 border border-amber-400/20
                              rounded-xl px-4 py-3">
                <div className="flex-1 min-w-0">
                  <p className="text-sm text-white font-medium truncate">
                    {user.displayName ?? user.email}
                  </p>
                  <p className="text-xs text-neutral-500 truncate">{user.email}</p>
                </div>
                <div className="flex gap-2 shrink-0">
                  <button
                    onClick={() => approveMut.mutate(user.id)}
                    disabled={approveMut.isPending}
                    className="px-3 py-1.5 bg-green-600 hover:bg-green-500 text-white text-xs
                               font-medium rounded-lg disabled:opacity-50 transition-colors"
                  >
                    Approve
                  </button>
                  <button
                    onClick={() => rejectMut.mutate(user.id)}
                    disabled={rejectMut.isPending}
                    className="px-3 py-1.5 bg-red-600/80 hover:bg-red-600 text-white text-xs
                               font-medium rounded-lg disabled:opacity-50 transition-colors"
                  >
                    Reject
                  </button>
                </div>
              </div>
            ))}
          </div>
        </section>

        {/* All users */}
        <section>
          <h2 className="text-sm font-semibold text-neutral-300 uppercase tracking-wide mb-3">
            All Users
          </h2>

          {loadingAll && <p className="text-neutral-500 text-sm">Loading…</p>}

          <div className="space-y-2">
            {allUsers?.map(user => (
              <div key={user.id}
                   className="flex items-center gap-4 bg-neutral-900 border border-neutral-800
                              rounded-xl px-4 py-3">
                <div className="w-8 h-8 rounded-full bg-neutral-700 flex items-center justify-center
                                text-white text-sm font-bold shrink-0">
                  {(user.displayName ?? user.email)[0].toUpperCase()}
                </div>
                <div className="flex-1 min-w-0">
                  <p className="text-sm text-white font-medium truncate">
                    {user.displayName ?? user.email}
                  </p>
                  <p className="text-xs text-neutral-500 truncate">{user.email}</p>
                </div>
                <span className={`text-xs px-2 py-0.5 rounded-full font-medium shrink-0
                  ${user.role === 'Admin'    ? 'bg-blue-600/30 text-blue-400' :
                    user.role === 'Approved' ? 'bg-green-600/20 text-green-400' :
                    user.role === 'Pending'  ? 'bg-amber-500/20 text-amber-400' :
                                               'bg-red-600/20 text-red-400'}`}>
                  {user.role}
                </span>
              </div>
            ))}
          </div>
        </section>
      </main>
    </div>
  );
}

function GraphSection() {
  const [result, setResult] = useState<{ indexed: number; message: string } | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError]     = useState('');

  const { data: stats, refetch } = useQuery({
    queryKey: ['graph-stats'],
    queryFn:  () => mediaApi.graphStats().then(r => r.data),
  });

  async function handleReindex() {
    setLoading(true); setError(''); setResult(null);
    try {
      const res = await mediaApi.graphReindex(500);
      setResult(res.data);
      refetch();
    } catch {
      setError('Reindex failed. Is the API running?');
    } finally {
      setLoading(false);
    }
  }

  return (
    <div className="space-y-3">
      {/* Stats */}
      {stats && (
        <div className="grid grid-cols-2 gap-2 text-xs">
          <div className="bg-neutral-800 rounded-lg px-3 py-2">
            <span className="text-neutral-500">Nodes</span>
            <div className="text-white font-semibold mt-0.5">
              {stats.nodes.total.toLocaleString()}
              <span className="text-neutral-500 font-normal ml-1">
                ({stats.nodes.photos} photos · {stats.nodes.tags} tags · {stats.nodes.locations} locations · {stats.nodes.events} events)
              </span>
            </div>
          </div>
          <div className="bg-neutral-800 rounded-lg px-3 py-2">
            <span className="text-neutral-500">Edges</span>
            <div className="text-white font-semibold mt-0.5">
              {stats.edges.total.toLocaleString()}
              <span className="text-neutral-500 font-normal ml-1">
                ({stats.edges.hasTag} hasTag · {stats.edges.relatedTo} relatedTo · {stats.edges.takenAt} takenAt)
              </span>
            </div>
          </div>
        </div>
      )}

      <button
        onClick={handleReindex}
        disabled={loading}
        className="px-4 py-2 bg-purple-700 hover:bg-purple-600 text-white text-sm font-medium
                   rounded-lg disabled:opacity-50 transition-colors"
      >
        {loading ? 'Indexing…' : '📊 Build / Refresh Graph Index'}
      </button>

      {result && <p className="text-sm text-green-400">✅ {result.message}</p>}
      {error   && <p className="text-sm text-red-400">{error}</p>}
    </div>
  );
}

function ReprocessButton() {
  const [result, setResult] = useState<{ queued: number; message: string } | null>(null);
  const [loading, setLoading] = useState(false);
  const [error,   setError]   = useState('');

  async function handleReprocess(forceAll: boolean) {
    setLoading(true); setError(''); setResult(null);
    try {
      const res = await mediaApi.reprocess(forceAll);
      setResult(res.data);
    } catch {
      setError('Failed to trigger reprocessing. Is the API running?');
    } finally {
      setLoading(false);
    }
  }

  return (
    <div className="space-y-2">
      <div className="flex gap-2 flex-wrap">
        <button
          onClick={() => handleReprocess(false)}
          disabled={loading}
          className="px-4 py-2 bg-purple-600 hover:bg-purple-500 text-white text-sm font-medium
                     rounded-lg disabled:opacity-50 transition-colors"
        >
          {loading ? 'Queuing…' : '⚡ Process Unprocessed Photos'}
        </button>
        <button
          onClick={() => handleReprocess(true)}
          disabled={loading}
          className="px-4 py-2 bg-neutral-700 hover:bg-neutral-600 text-white text-sm font-medium
                     rounded-lg disabled:opacity-50 transition-colors"
        >
          🔄 Reprocess All Photos
        </button>
      </div>
      {result && (
        <p className="text-sm text-green-400">✅ {result.message}</p>
      )}
      {error && (
        <p className="text-sm text-red-400">{error}</p>
      )}
    </div>
  );
}
