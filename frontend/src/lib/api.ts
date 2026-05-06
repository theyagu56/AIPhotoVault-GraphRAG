import axios from 'axios';

export const API_BASE = process.env.NEXT_PUBLIC_API_URL ?? 'http://localhost:5050';

const api = axios.create({ baseURL: API_BASE });

// Attach JWT from localStorage on every request
api.interceptors.request.use((config) => {
  const token = typeof window !== 'undefined' ? localStorage.getItem('pv_token') : null;
  if (token) config.headers.Authorization = `Bearer ${token}`;
  return config;
});

// ── Types ────────────────────────────────────────────────────
export interface Tag {
  id: string;
  name: string;
  category: string;
  confidence?: number;
  source: string;
}

export interface Media {
  id: string;
  fileName: string;
  originalPath: string;
  mediaType: 'Photo' | 'Video' | 'Unknown';
  mimeType?: string;
  fileSizeBytes?: number;
  width?: number;
  height?: number;
  durationSeconds?: number;
  capturedAt?: string;
  aiProcessed: boolean;
  aiModelUsed?: string;
  inTrash: boolean;
  isBlurry?: boolean;
  blurScore?: number;
  isDuplicate?: boolean;
  duplicateOfId?: string;
  latitude?: number;
  longitude?: number;
  cameraModel?: string;
}

export interface PagedResult<T> {
  page: number;
  pageSize: number;
  total: number;
  totalPages: number;
  items: T[];
}

export interface Album {
  id: string;
  name: string;
  description?: string;
  albumType: string;
  createdAt: string;
  media: Media[];
}

export interface User {
  id: string;
  email: string;
  displayName?: string;
  profilePicUrl?: string;
  role: string;
}

export interface GraphSearchResult {
  query:        string;
  expandedTags: string[];   // tags the graph also searched via relatedTo edges
  total:        number;
  page:         number;
  pageSize:     number;
  totalPages:   number;
  items:        Media[];
}

export interface Stats {
  totalPhotos: number;
  totalVideos: number;
  totalMedia: number;
  inTrash: number;
  mediaRoot: string;
}

export interface ScanResult {
  path: string;
  discovered: number;
  imported: number;
  skipped: number;
  failed: number;
}

// ── Auth ─────────────────────────────────────────────────────
export const authApi = {
  devLogin: (email: string) =>
    api.post<{ token: string; user: User }>('/api/auth/dev-login', { email }),
  googleLogin: (idToken: string) =>
    api.post<{ status: string; token?: string; user?: User }>('/api/auth/google', { idToken }),
  me: () => api.get<User>('/api/auth/me'),
};

// ── Media ────────────────────────────────────────────────────
export const mediaApi = {
  list: (params?: {
    page?: number; pageSize?: number; search?: string;
    tag?: string; albumId?: string; mediaType?: string;
    inTrash?: boolean; sortBy?: string; desc?: boolean;
  }) => api.get<PagedResult<Media>>('/api/media', { params }),

  getById:    (id: string)  => api.get<Media>(`/api/media/${id}`),
  stats:      ()            => api.get<Stats>('/api/media/stats'),
  scan:       (path: string) => api.post<ScanResult>('/api/media/scan', { path }),
  trash:      (id: string)  => api.delete(`/api/media/${id}`),
  restore:    (id: string)  => api.post(`/api/media/${id}/restore`),
  getTags:    (id: string)  => api.get<{ tags: Tag[]; caption: string | null }>(`/api/media/${id}/tags`),
  blurry:     (page = 1, pageSize = 48) => api.get<PagedResult<Media>>('/api/media/blurry', { params: { page, pageSize } }),
  duplicates: (page = 1, pageSize = 48) => api.get<PagedResult<Media>>('/api/media/duplicates', { params: { page, pageSize } }),
  reprocess:   (all = false, batchSize = 1000) =>
    api.post<{ queued: number; message: string }>('/api/media/reprocess', null, { params: { all, batchSize } }),
  graphStats:  () =>
    api.get<{ nodes: { total: number; photos: number; tags: number; locations: number; events: number };
               edges: { total: number; hasTag: number; relatedTo: number; takenAt: number } }>('/api/media/graph/stats'),
  graphReindex: (batchSize = 500) =>
    api.post<{ indexed: number; message: string }>('/api/media/graph/reindex', null, { params: { batchSize } }),
  graphSearch: (params: { q: string; maxHops?: number; page?: number; pageSize?: number }) =>
    api.get<GraphSearchResult>('/api/media/graph/search', { params }),
  similar: (id: string, limit = 12) =>
    api.get<{ items: Media[] }>(`/api/media/${id}/similar`, { params: { limit } }),

  thumbnailUrl: (id: string, size: 'sm' | 'md' | 'lg' = 'md') =>
    `${API_BASE}/api/media/${id}/thumbnail/${size}`,

  getAlbums:    (id: string) => api.get<Album[]>(`/api/media/${id}/albums`),
  refreshExif:  (id: string) => api.post(`/api/media/${id}/refresh-exif`),

  // For direct image src — use original via API proxy (Phase 3)
  imageUrl: (media: Media) => `${API_BASE}/api/media/${media.id}/file`,
};

// ── Albums ───────────────────────────────────────────────────
export const albumApi = {
  list:        (userId?: string)                         => api.get<Album[]>('/api/album', { params: { userId } }),
  getById:     (id: string)                              => api.get<Album>(`/api/album/${id}`),
  create:      (name: string, userId?: string)           => api.post<Album>('/api/album', { name, userId }),
  delete:      (id: string)                              => api.delete(`/api/album/${id}`),
  addMedia:    (albumId: string, mediaId: string)        => api.post(`/api/album/${albumId}/media/${mediaId}`),
  removeMedia: (albumId: string, mediaId: string)        => api.delete(`/api/album/${albumId}/media/${mediaId}`),
};

// ── Users (admin) ────────────────────────────────────────────
export const userApi = {
  all:     ()           => api.get<User[]>('/api/user'),
  pending: ()           => api.get<User[]>('/api/user/pending'),
  approve: (id: string) => api.post(`/api/user/${id}/approve`),
  reject:  (id: string) => api.post(`/api/user/${id}/reject`),
};

// ── Status ───────────────────────────────────────────────────
export const statusApi = {
  get: () => api.get('/api/status'),
};

export default api;
