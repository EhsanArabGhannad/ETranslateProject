import { tiptapProvider } from './tiptap-provider.js';

// Deliberate build-time selection; never pick an engine from a query string or stored native JSON.
// A new provider must implement the session contract and pass preservation/print gates first.
export const defaultEditorProvider = tiptapProvider;
