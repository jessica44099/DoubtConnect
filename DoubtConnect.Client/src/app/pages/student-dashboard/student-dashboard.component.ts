
import { CommonModule } from '@angular/common';
import { HttpClient, HttpHeaders } from '@angular/common/http';
import { Component, OnInit, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';

interface StudentProfileResponse {
  userId: string;
  fullName: string;
  email: string;
  role: string;
  profile: {
    educationLevel: string | null;
    totalSessionsAttended: number;
  } | null;
}

interface StudentBooking {
  id: string;
  mentorId: string;
  mentorName: string;
  slotStartUtc: string;
  slotEndUtc: string;
  mode: number;
  doubtTitle: string;
  doubtDescription: string;
  status: string;
  meetingLink?: string | null;
  rejectionReason?: string | null;
}

@Component({
  selector: 'app-student-dashboard',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './student-dashboard.component.html',
  styleUrl: './student-dashboard.component.css'
})
export class StudentDashboardComponent implements OnInit {
  private readonly http = inject(HttpClient);
  private readonly router = inject(Router);

  private readonly apiUrl = 'http://localhost:5234/api';

  studentName = 'Student';
  email = '';
  educationLevel = '';
  totalSessions = 0;

  bookings: StudentBooking[] = [];
  loading = true;
  savingProfile = false;
  errorMessage = '';
  successMessage = '';
  activeFilter = 'All';

  private headers(): HttpHeaders {
    return new HttpHeaders({
      Authorization:
        `Bearer ${sessionStorage.getItem('accessToken') ?? ''}`
    });
  }

  ngOnInit(): void {
    this.loadDashboard();
  }

  loadDashboard(): void {
    this.loading = true;
    this.errorMessage = '';

    this.http.get<StudentProfileResponse>(
      `${this.apiUrl}/profiles/me`,
      { headers: this.headers() }
    ).subscribe({
      next: profile => {
        this.studentName = profile.fullName || 'Student';
        this.email = profile.email || '';
        this.educationLevel =
          profile.profile?.educationLevel ?? '';
        this.totalSessions =
          profile.profile?.totalSessionsAttended ?? 0;
      },
      error: () => {
        this.errorMessage =
          'Could not load your profile. Please log in again if your session expired.';
      }
    });

    this.http.get<StudentBooking[]>(
      `${this.apiUrl}/bookings/mine`,
      { headers: this.headers() }
    ).subscribe({
      next: bookings => {
        this.bookings = bookings.filter(
          booking => booking.status !== 'Cancelled'
        );
        this.loading = false;
      },
      error: () => {
        this.loading = false;
        this.errorMessage =
          'Could not load your sessions. Check that the API is running and you are logged in.';
      }
    });
  }

  get filteredBookings(): StudentBooking[] {
    if (this.activeFilter === 'All') {
      return this.bookings;
    }

    return this.bookings.filter(
      booking =>
        booking.status.toLowerCase() ===
        this.activeFilter.toLowerCase()
    );
  }

  get pendingCount(): number {
    return this.bookings.filter(
      booking => booking.status.toLowerCase() === 'pending'
    ).length;
  }

  get upcomingCount(): number {
    const now = Date.now();

    return this.bookings.filter(booking =>
      ['approved', 'ongoing'].includes(
        booking.status.toLowerCase()
      ) &&
      new Date(booking.slotStartUtc).getTime() >= now
    ).length;
  }

  saveProfile(): void {
    this.savingProfile = true;
    this.errorMessage = '';
    this.successMessage = '';

    this.http.put(
      `${this.apiUrl}/profiles/student`,
      { educationLevel: this.educationLevel },
      { headers: this.headers() }
    ).subscribe({
      next: () => {
        this.savingProfile = false;
        this.successMessage = 'Profile updated successfully.';
      },
      error: err => {
        this.savingProfile = false;
        this.errorMessage =
          err.error?.message ?? 'Unable to update your profile.';
      }
    });
  }

  setFilter(filter: string): void {
    this.activeFilter = filter;
  }

  statusClass(status: string): string {
    return status.toLowerCase().replace(/\s+/g, '-');
  }

  modeLabel(mode: number): string {
    switch (mode) {
      case 1: return 'Video call';
      case 2: return 'Phone call';
      case 3: return 'Text chat';
      default: return 'Session';
    }
  }

  logout(): void {
    sessionStorage.removeItem('accessToken');
    sessionStorage.removeItem('user');
    this.router.navigate(['/login']);
  }
}
