export interface Page { id: string; title: string; route: string; renderer: string; views?: ViewBlock[] }
export interface Panel { id: string; title: string; position: string; renderer: string; pages?: string[] }
export interface Observation { type: string; sourceId?: string; timestamp: string }
export interface Plugin { id: string; name: string; type: string; state: string; controllable: boolean; config?: Record<string,string> }
export interface ViewColumn { key: string; title: string }
export interface ViewField { key: string; label: string }
export interface ViewAction { label: string; command: string; args?: Record<string,string> }
export interface ViewBlock {
  kind: string; query?: string; params?: Record<string,string>;
  columns?: ViewColumn[]; fields?: ViewField[];
  rowActions?: ViewAction[]; selectFocus?: boolean;
  titleKey?: string; pageSize?: number;
}
export interface PageAction { label: string; command: string; datePicker?: boolean }
