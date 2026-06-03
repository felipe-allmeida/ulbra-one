import { describe, it, expect } from 'vitest';
import { brl } from './lib';
describe('brl', () => { it('formats BRL', () => { expect(brl(1000)).toContain('R$'); }); });
