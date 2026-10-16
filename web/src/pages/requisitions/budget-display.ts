/** Percentual limitado a [0,100] para larguras de barra (teto estourado não transborda a trilha). */
export const clampPct = (n: number): number => Math.max(0, Math.min(100, n));
