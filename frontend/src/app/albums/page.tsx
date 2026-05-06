'use client';
import { useState, useEffect } from 'react';
import { useRouter } from 'next/navigation';
import Link from 'next/link';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import Sidebar from '@/components/Sidebar';
import { albumApi, mediaApi } from '@/lib/api';

export default function AlbumsPage() {
  const router = useRouter();
  const qc     = useQueryClient();
  const [creating, setCreating] = useState(false);
  const [newName,  setNewName]  = useState('');

  useEffect(() => {
    if (typeof window !== 'undefined' && !localStorage.getItem('pv_token')) {
      router.replace('/login');
    }
  }, [router]);

  const { data: albums, isLoading } = useQuery({
    queryKey: ['albums'],
    queryFn:  () => albumApi.list().then(r => r.data),
  });

  const createMut = useMutation({
    mutationFn: (name: string) => albumApi.create(name),
    onSuccess:  () => {
      qc.invalidateQueries({ queryKey: ['albums'] });
      setNewName('');
      setCreating(false);
    },
  });

  function handleCreate(e: React.FormEvent) {
    e.preventDefault();
    if (newName.trim()) createMut.mutate(newName.trim());
  }

  return (
    <div className="flex min-h-screen bg-neutral-950">
      <Sidebar />
      <main className="flex-1 p-6">
        <div className="flex items-center justify-between mb-6">
          <h1 className="text-xl font-bold text-white">Albums</h1>
          <button
            onClick={() => setCreating(true)}
            className="px-4 py-2 bg-blue-600 hover:bg-blue-500 text-white text-sm font-medium
                       rounded-lg transition-colors"
          >
            + New Album
          </button>
        </div>

        {/* Create form */}
        {creating && (
          <form onSubmit={handleCreate}
                className="mb-6 flex gap-2">
            <input
              autoFocus
              type="text"
              value={newName}
              onChange={e => setNewName(e.target.value)}
              placeholder="Album name"
              className="bg-neutral-800 border border-neutral-700 rounded-lg px-4 py-2
                         text-white text-sm placeholder-neutral-500 focus:outline-none
                         focus:border-blue-500 w-64"
            />
            <button type="submit"
                    disabled={createMut.isPending}
                    className="px-4 py-2 bg-blue-600 hover:bg-blue-500 text-white text-sm
                               rounded-lg disabled:opacity-50">
              Create
            </button>
            <button type="button" onClick={() => setCreating(false)}
                    className="px-4 py-2 bg-neutral-800 text-neutral-400 text-sm rounded-lg
                               hover:text-white">
              Cancel
            </button>
          </form>
        )}

        {isLoading && (
          <div className="text-neutral-500 text-sm">Loading albums…</div>
        )}

        {!isLoading && albums?.length === 0 && (
          <div className="flex flex-col items-center justify-center h-48 text-neutral-500 gap-2">
            <span className="text-3xl">📁</span>
            <span className="text-sm">No albums yet. Create one to organize your photos.</span>
          </div>
        )}

        <div className="grid grid-cols-2 sm:grid-cols-3 md:grid-cols-4 lg:grid-cols-5 gap-4">
          {albums?.map(album => (
            <Link
              key={album.id}
              href={`/albums/${album.id}`}
              className="group bg-neutral-900 border border-neutral-800 rounded-xl overflow-hidden
                         hover:border-neutral-600 transition-colors"
            >
              {/* Thumbnail grid */}
              <div className="grid grid-cols-2 gap-0.5 aspect-square bg-neutral-800">
                {album.media.slice(0, 4).map((m, i) => (
                  <div key={m.id} className="relative overflow-hidden bg-neutral-700">
                    <img
                      src={mediaApi.thumbnailUrl(m.id, 'sm')}
                      alt=""
                      className="w-full h-full object-cover"
                    />
                  </div>
                ))}
                {album.media.length === 0 && (
                  <div className="col-span-2 row-span-2 flex items-center justify-center
                                  text-3xl text-neutral-600">
                    📷
                  </div>
                )}
              </div>

              <div className="p-3">
                <p className="text-sm font-medium text-white truncate
                               group-hover:text-blue-400 transition-colors">
                  {album.name}
                </p>
                <p className="text-xs text-neutral-500 mt-0.5">
                  {album.media.length} {album.media.length === 1 ? 'item' : 'items'}
                </p>
              </div>
            </Link>
          ))}
        </div>
      </main>
    </div>
  );
}
