
import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpHeaders } from '@angular/common/http';
import { Observable } from 'rxjs';

export interface MentorCalendarEvent {
  id: string;
  date: string;
  startTime: string;
  endTime: string;
  activityType: string;
  title: string;
  studentName: string | null;
}

export interface CreateMentorCalendarEvent {
  date: string;
  startTime: string;
  endTime: string;
  activityType: string;
  title: string;
  studentName?: string | null;
}

@Injectable({
  providedIn: 'root'
})
export class MentorCalendarEventService {
  private readonly http = inject(HttpClient);
  private readonly apiUrl =
    'http://localhost:5234/api/mentor-calendar-events';

  private getAuthHeaders(): HttpHeaders {
    const token = sessionStorage.getItem('accessToken');

    return new HttpHeaders({
      Authorization: `Bearer ${token ?? ''}`
    });
  }

  getEvents(from: string, to: string): Observable<MentorCalendarEvent[]> {
    return this.http.get<MentorCalendarEvent[]>(this.apiUrl, {
      headers: this.getAuthHeaders(),
      params: { from, to }
    });
  }

  createEvent(
    event: CreateMentorCalendarEvent
  ): Observable<MentorCalendarEvent> {
    return this.http.post<MentorCalendarEvent>(
      this.apiUrl,
      event,
      { headers: this.getAuthHeaders() }
    );
  }

  deleteEvent(id: string): Observable<void> {
    return this.http.delete<void>(
      `${this.apiUrl}/${id}`,
      { headers: this.getAuthHeaders() }
    );
  }
}
