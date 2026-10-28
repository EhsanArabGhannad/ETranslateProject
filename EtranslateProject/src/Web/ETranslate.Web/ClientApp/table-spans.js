// Logical grid columns; physical cell count can be smaller after a horizontal merge.
export const columnCount = cells => cells.reduce((sum, cell) => sum + (cell.attrs?.colspan ?? 1), 0);
