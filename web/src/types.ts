export type Role = 'Requester' | 'Approver' | 'Auditor';
export type Status = 'Pending' | 'Approved' | 'Rejected' | 'Cancelled';
export interface User { id: string; name: string; role: Role; email: string }
export interface Session { authenticated: boolean; user: User | null; csrfToken: string; demo: boolean }
export interface Resource { key: string; name: string; system: string; description: string }
export interface RequestRow { id: string; title: string; resourceKey: string; resourceName: string; system: string; scope: string; reason: string; requesterId: string; requesterName: string; status: Status; version: number; createdAt: string; updatedAt: string }
export interface AuditEvent { id: string; requestId: string; actorName: string; actorRole: Role; action: string; summary: string; occurredAt: string }
export interface RequestList { items: RequestRow[]; stats: { total: number; pending: number; approved: number; rejected: number } }
export interface Detail { request: RequestRow; events: AuditEvent[] }
