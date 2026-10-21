
import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpHeaders } from '@angular/common/http';
import { Observable } from 'rxjs';

export interface WeeklyAvailabilityBlock {
  dayOfWeek: number;
  startTime: string;
  endTime: string;
  isFree: boolean;
  activityType: string;
  title: string | null;
}

@Injectable({
  providedIn: 'root'
})
export class WeeklyAvailabilityService {
  private readonly http = inject(HttpClient);
  private readonly apiUrl = 'http://localhost:5234/api/weekly-availability';

  private getAuthHeaders(): HttpHeaders {
    const token = sessionStorage.getItem('accessToken');

    return new HttpHeaders({
      Authorization: `Bearer ${token ?? ''}`
    });
  }

  getWeeklyAvailability(): Observable<WeeklyAvailabilityBlock[]> {
    return this.http.get<WeeklyAvailabilityBlock[]>(
      this.apiUrl,
      { headers: this.getAuthHeaders() }
    );
  }

  saveWeeklyAvailability(
    blocks: WeeklyAvailabilityBlock[]
  ): Observable<{ message: string; blocksSaved: number }> {
    return this.http.put<{ message: string; blocksSaved: number }>(
      this.apiUrl,
      blocks,
      { headers: this.getAuthHeaders() }
    );
  }
}
