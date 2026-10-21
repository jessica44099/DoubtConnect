
import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpHeaders } from '@angular/common/http';
import { Observable } from 'rxjs';

export interface AvailabilitySlot {
  id: string;
  mentorId: string;
  startTimeUtc: string;
  endTimeUtc: string;
  status: number;
  createdAtUtc?: string;
}

export interface CreateAvailabilityRequest {
  startTimeUtc: string;
  endTimeUtc: string;
}

@Injectable({
  providedIn: 'root'
})
export class AvailabilityService {
  private readonly http = inject(HttpClient);
  private readonly apiUrl = 'http://localhost:5234/api/availability';

  private getAuthHeaders(): HttpHeaders {
    const token = sessionStorage.getItem('accessToken');

    return new HttpHeaders({
      Authorization: `Bearer ${token ?? ''}`
    });
  }

  getMySlots(): Observable<AvailabilitySlot[]> {
    return this.http.get<AvailabilitySlot[]>(
      `${this.apiUrl}/mine`,
      { headers: this.getAuthHeaders() }
    );
  }

  createSlot(
    request: CreateAvailabilityRequest
  ): Observable<AvailabilitySlot> {
    return this.http.post<AvailabilitySlot>(
      this.apiUrl,
      request,
      { headers: this.getAuthHeaders() }
    );
  }

  updateSlot(
    id: string,
    request: CreateAvailabilityRequest & { status: number }
  ): Observable<AvailabilitySlot> {
    return this.http.put<AvailabilitySlot>(
      `${this.apiUrl}/${id}`,
      request,
      { headers: this.getAuthHeaders() }
    );
  }

  deleteSlot(id: string): Observable<void> {
    return this.http.delete<void>(
      `${this.apiUrl}/${id}`,
      { headers: this.getAuthHeaders() }
    );
  }
}
