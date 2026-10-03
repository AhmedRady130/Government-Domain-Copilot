import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../environments/environment';
import { DocumentDetail, IngestDocumentRequest, IngestDocumentResponse } from '../models/api.models';
@Injectable({ providedIn: 'root' })
export class DocumentService {
  private readonly root = environment.apiUrl.replace(/\/$/, '');
  constructor(private readonly http: HttpClient) {}
  ingest(request: IngestDocumentRequest): Observable<IngestDocumentResponse> { return this.http.post<IngestDocumentResponse>(`${this.root}/api/documents`, request); }
  get(id: string): Observable<DocumentDetail> { return this.http.get<DocumentDetail>(`${this.root}/api/documents/${encodeURIComponent(id)}`); }
}
