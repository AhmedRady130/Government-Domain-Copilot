import { TestBed } from '@angular/core/testing';
import { ApiKeyService } from './api-key.service';

describe('ApiKeyService', () => {
  it('keeps a trimmed development credential in memory without browser storage writes', () => {
    const storageWrite = spyOn(Storage.prototype, 'setItem');
    const service = TestBed.inject(ApiKeyService);
    service.set(' temporary-key ');
    expect(service.key()).toBe('temporary-key');
    expect(storageWrite).not.toHaveBeenCalled();
  });
});
