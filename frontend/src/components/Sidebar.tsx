'use client';
import Link from 'next/link';
import { usePathname, useRouter } from 'next/navigation';
import { useQuery } from '@tanstack/react-query';
import { mediaApi, authApi, User } from '@/lib/api';

const navItems = [
  { href: '/photos',     label: 'Photos',     icon: '🖼️' },
  { href: '/albums',     label: 'Albums',     icon: '📁' },
  { href: '/duplicates', label: 'Duplicates', icon: '🔁' },
  { href: '/blurry',     label: 'Blurry',     icon: '🌫️' },
  { href: '/trash',      label: 'Trash',      icon: '🗑️' },
  { href: '/admin',      label: 'Admin',      icon: '⚙️' },
];

export default function Sidebar() {
  const pathname  = usePathname();
  const router    = useRouter();

  const { data: stats } = useQuery({
    queryKey: ['stats'],
    queryFn:  () => mediaApi.stats().then(r => r.data),
  });

  const { data: user } = useQuery({
    queryKey: ['me'],
    queryFn:  () => authApi.me().then(r => r.data),
    retry: false,
  });

  function logout() {
    localStorage.removeItem('pv_token');
    localStorage.removeItem('pv_user');
    router.push('/login');
  }

  return (
    <aside className="w-56 shrink-0 h-screen sticky top-0 flex flex-col bg-neutral-900
                      border-r border-neutral-800 py-6 px-3">
      {/* Logo */}
      <div className="flex items-center gap-2 px-3 mb-8">
        <span className="text-2xl">📷</span>
        <span className="text-lg font-bold text-white tracking-tight">PhotoVault</span>
      </div>

      {/* Nav */}
      <nav className="flex-1 space-y-1">
        {navItems.map(({ href, label, icon }) => {
          const active = pathname.startsWith(href);
          return (
            <Link
              key={href}
              href={href}
              className={`flex items-center gap-3 px-3 py-2 rounded-lg text-sm font-medium transition-colors
                ${active
                  ? 'bg-blue-600/20 text-blue-400'
                  : 'text-neutral-400 hover:text-white hover:bg-neutral-800'
                }`}
            >
              <span>{icon}</span>
              <span>{label}</span>
            </Link>
          );
        })}
      </nav>

      {/* Stats */}
      {stats && (
        <div className="px-3 py-3 bg-neutral-800/50 rounded-lg mb-4 space-y-1 text-xs text-neutral-400">
          <div className="flex justify-between">
            <span>Photos</span>
            <span className="text-white font-medium">{stats.totalPhotos.toLocaleString()}</span>
          </div>
          <div className="flex justify-between">
            <span>Videos</span>
            <span className="text-white font-medium">{stats.totalVideos.toLocaleString()}</span>
          </div>
          <div className="flex justify-between">
            <span>Trash</span>
            <span className="text-neutral-500">{stats.inTrash.toLocaleString()}</span>
          </div>
        </div>
      )}

      {/* User */}
      <div className="px-3 space-y-2">
        {user && (
          <div className="flex items-center gap-2 text-xs text-neutral-400 truncate">
            <div className="w-6 h-6 rounded-full bg-blue-600 flex items-center justify-center
                            text-white text-xs font-bold shrink-0">
              {(user.displayName ?? user.email)[0].toUpperCase()}
            </div>
            <span className="truncate">{user.displayName ?? user.email}</span>
          </div>
        )}
        <button
          onClick={logout}
          className="w-full text-xs text-neutral-500 hover:text-red-400 transition-colors text-left px-1"
        >
          Sign out
        </button>
      </div>
    </aside>
  );
}
