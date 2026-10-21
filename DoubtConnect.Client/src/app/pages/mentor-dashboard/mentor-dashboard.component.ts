
import { CommonModule } from '@angular/common';
import {
  HttpClient,
  HttpHeaders,
  HttpErrorResponse
} from '@angular/common/http';
import { Component, OnInit } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';

import {
  WeeklyAvailabilityService,
  WeeklyAvailabilityBlock
} from '../../services/weekly-availability.service';

import {
  MentorCalendarEventService,
  MentorCalendarEvent
} from '../../services/mentor-calendar-event.service';

interface CalendarDay {
  date: Date;
  dayOfWeek: number;
  name: string;
  dateLabel: number;
  isToday: boolean;
}

interface MentorProfileResponse {
  profile?: {
    expertise?: string | null;
    bio?: string | null;
  } | null;
}

@Component({
  selector: 'app-mentor-dashboard',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterLink],
  templateUrl: './mentor-dashboard.component.html',
  styleUrl: './mentor-dashboard.component.css'
})
export class MentorDashboardComponent implements OnInit {
  private readonly apiUrl = 'http://localhost:5234/api';

  readonly daysOfWeek = [
    'Sunday',
    'Monday',
    'Tuesday',
    'Wednesday',
    'Thursday',
    'Friday',
    'Saturday'
  ];

  readonly hours = Array.from({ length: 10 }, (_, i) => i + 10);

  readonly activityTypes = [
    'Doubt Session',
    'Meeting',
    'Class',
    'Break',
    'Personal',
    'Leave',
    'Other'
  ];

  readonly mentorSubjects = ['Quant', 'VARC', 'DILR'];

  // Dashboard navigation
  activeSection: 'profile' | 'calendar' | 'sessions' = 'profile';

  // Weekly calendar
  weeklyBlocks: WeeklyAvailabilityBlock[] = [];
  manualEvents: MentorCalendarEvent[] = [];
  calendarDays: CalendarDay[] = [];

  selectedDate = '';
  eventStartTime = '10:00';
  eventEndTime = '11:00';
  eventType = 'Meeting';
  eventTitle = '';
  studentName = '';
  isEventEditorOpen = false;

  currentWeekStart = this.getSunday(new Date());
  today = new Date();

  isLoading = false;
  isSaving = false;
  errorMessage = '';
  successMessage = '';

  // Mentor profile
  profileExpertise = '';
  selectedSubjects: string[] = [];
  profileBio = '';
  profileLoading = false;
  profileSaving = false;

  constructor(
    private router: Router,
    private weeklyAvailabilityService: WeeklyAvailabilityService,
    private mentorCalendarEventService: MentorCalendarEventService,
    private http: HttpClient
  ) {}

  ngOnInit(): void {
    this.buildCalendarDays();
    this.loadWeeklyAvailability();
    this.loadManualEvents();
    this.loadMentorProfile();
  }

  // -------------------------
  // Authentication
  // -------------------------

  private getAuthHeaders(): HttpHeaders {
    const token = sessionStorage.getItem('accessToken');

    return new HttpHeaders({
      Authorization: `Bearer ${token ?? ''}`
    });
  }

  logout(): void {
    sessionStorage.removeItem('accessToken');
    sessionStorage.removeItem('user');
    this.router.navigate(['/login']);
  }

  // -------------------------
  // Dashboard navigation
  // -------------------------

  showSection(section: 'profile' | 'calendar' | 'sessions'): void {
    this.activeSection = section;
    this.errorMessage = '';
    this.successMessage = '';

    if (section === 'calendar') {
      this.buildCalendarDays();
      this.loadWeeklyAvailability();
      this.loadManualEvents();
    }

    if (section === 'profile') {
      this.loadMentorProfile();
    }
  }

  // -------------------------
  // Calendar date helpers
  // -------------------------

  private getSunday(date: Date): Date {
    const result = new Date(date);
    result.setHours(0, 0, 0, 0);
    result.setDate(result.getDate() - result.getDay());
    return result;
  }

  private sameDate(a: Date, b: Date): boolean {
    return (
      a.getFullYear() === b.getFullYear() &&
      a.getMonth() === b.getMonth() &&
      a.getDate() === b.getDate()
    );
  }

  private formatDateForApi(date: Date): string {
    const year = date.getFullYear();
    const month = String(date.getMonth() + 1).padStart(2, '0');
    const day = String(date.getDate()).padStart(2, '0');

    return `${year}-${month}-${day}`;
  }

  buildCalendarDays(): void {
    this.calendarDays = Array.from({ length: 7 }, (_, index) => {
      const date = new Date(this.currentWeekStart);
      date.setDate(date.getDate() + index);

      return {
        date,
        dayOfWeek: date.getDay(),
        name: this.daysOfWeek[date.getDay()],
        dateLabel: date.getDate(),
        isToday: this.sameDate(date, this.today)
      };
    });
  }

  get weekLabel(): string {
    const first = this.calendarDays[0]?.date ?? this.currentWeekStart;
    const last = this.calendarDays[6]?.date ?? this.currentWeekStart;

    const firstMonth = first.toLocaleDateString('en-IN', {
      month: 'short'
    });

    const lastMonth = last.toLocaleDateString('en-IN', {
      month: 'short'
    });

    if (first.getFullYear() !== last.getFullYear()) {
      return `${first.toLocaleDateString('en-IN', {
        month: 'short',
        year: 'numeric'
      })} – ${last.toLocaleDateString('en-IN', {
        month: 'short',
        year: 'numeric'
      })}`;
    }

    if (first.getMonth() === last.getMonth()) {
      return `${first.getDate()}–${last.getDate()} ${firstMonth} ${first.getFullYear()}`;
    }

    return `${first.getDate()} ${firstMonth} – ${last.getDate()} ${lastMonth} ${last.getFullYear()}`;
  }

  previousWeek(): void {
    this.currentWeekStart.setDate(this.currentWeekStart.getDate() - 7);
    this.currentWeekStart = new Date(this.currentWeekStart);
    this.buildCalendarDays();
    this.loadManualEvents();
  }

  nextWeek(): void {
    this.currentWeekStart.setDate(this.currentWeekStart.getDate() + 7);
    this.currentWeekStart = new Date(this.currentWeekStart);
    this.buildCalendarDays();
    this.loadManualEvents();
  }

  goToToday(): void {
    this.today = new Date();
    this.currentWeekStart = this.getSunday(this.today);
    this.buildCalendarDays();
    this.loadManualEvents();
  }

  // -------------------------
  // Mentor profile
  // -------------------------

  loadMentorProfile(): void {
    this.profileLoading = true;

    this.http.get<MentorProfileResponse>(
      `${this.apiUrl}/profiles/me`,
      { headers: this.getAuthHeaders() }
    ).subscribe({
      next: response => {
        const profile = response.profile;

        this.profileExpertise = profile?.expertise ?? '';

        this.selectedSubjects = this.profileExpertise
          .split(',')
          .map(subject => subject.trim())
          .filter(subject => this.mentorSubjects.includes(subject));

        this.profileBio = profile?.bio ?? '';
        this.profileLoading = false;
      },
      error: (error: HttpErrorResponse) => {
        this.profileLoading = false;
        this.errorMessage =
          error.error?.message ??
          'Could not load your mentor profile. Please sign in again if your session has expired.';
      }
    });
  }

  toggleSubject(subject: string, checked: boolean): void {
    if (!this.mentorSubjects.includes(subject)) {
      return;
    }

    if (checked) {
      if (!this.selectedSubjects.includes(subject)) {
        this.selectedSubjects = [
          ...this.selectedSubjects,
          subject
        ];
      }
    } else {
      this.selectedSubjects = this.selectedSubjects.filter(
        item => item !== subject
      );
    }

    this.profileExpertise = this.selectedSubjects.join(', ');
  }

  saveMentorProfile(): void {
    this.errorMessage = '';
    this.successMessage = '';

    const selectedSubjects = this.mentorSubjects.filter(
      subject => this.selectedSubjects.includes(subject)
    );

    if (selectedSubjects.length === 0) {
      this.errorMessage = 'Please select at least one teaching subject.';
      return;
    }

    if (this.profileBio.length > 2000) {
      this.errorMessage = 'Bio cannot exceed 2000 characters.';
      return;
    }

    this.profileSaving = true;

    this.http.put(
      `${this.apiUrl}/profiles/mentor`,
      {
        expertise: selectedSubjects.join(', '),
        bio: this.profileBio.trim() || null
      },
      { headers: this.getAuthHeaders() }
    ).subscribe({
      next: () => {
        this.profileSaving = false;
        this.profileExpertise = selectedSubjects.join(', ');
        this.selectedSubjects = [...selectedSubjects];
        this.successMessage = 'Mentor profile saved successfully.';
      },
      error: (error: HttpErrorResponse) => {
        this.profileSaving = false;
        this.errorMessage =
          error.error?.message ??
          'Could not save your mentor profile. Please try again.';
      }
    });
  }

  // -------------------------
  // Weekly availability
  // -------------------------

  loadWeeklyAvailability(): void {
    this.isLoading = true;
    this.errorMessage = '';

    this.weeklyAvailabilityService.getWeeklyAvailability().subscribe({
      next: blocks => {
        this.weeklyBlocks = blocks.map(block => ({ ...block }));
        this.isLoading = false;
      },
      error: (error: HttpErrorResponse) => {
        this.isLoading = false;
        this.errorMessage =
          error.error?.message ||
          'Could not load your timetable. Please sign in again if your session has expired.';
      }
    });
  }

  saveWeeklyAvailability(): void {
    this.errorMessage = '';
    this.successMessage = '';

    for (const block of this.weeklyBlocks) {
      if (
        !block.startTime ||
        !block.endTime ||
        block.startTime >= block.endTime
      ) {
        this.errorMessage =
          'Every event must have a valid start and end time.';
        return;
      }

      if (block.startTime < '10:00' || block.endTime > '20:00') {
        this.errorMessage =
          'Events must be within working hours (10 AM–8 PM).';
        return;
      }

      block.isFree = block.activityType === 'Free';
    }

    for (const day of this.daysOfWeek) {
      const dayNumber = this.daysOfWeek.indexOf(day);

      const blocks = this.weeklyBlocks
        .filter(block =>
          block.dayOfWeek === dayNumber &&
          block.activityType !== 'Free'
        )
        .sort((a, b) => a.startTime.localeCompare(b.startTime));

      for (let i = 1; i < blocks.length; i++) {
        if (blocks[i].startTime < blocks[i - 1].endTime) {
          this.errorMessage =
            `Events overlap on ${day}. Please adjust their times.`;
          return;
        }
      }
    }

    this.isSaving = true;

    this.weeklyAvailabilityService
      .saveWeeklyAvailability(this.weeklyBlocks)
      .subscribe({
        next: response => {
          this.isSaving = false;
          this.successMessage =
            `${response.message} (${response.blocksSaved} events saved).`;
          this.loadWeeklyAvailability();
        },
        error: (error: HttpErrorResponse) => {
          this.isSaving = false;
          this.errorMessage =
            error.error?.message || 'Could not save the calendar.';
        }
      });
  }

  getBusyBlocks(dayOfWeek: number): WeeklyAvailabilityBlock[] {
    return this.weeklyBlocks
      .filter(block =>
        block.dayOfWeek === dayOfWeek &&
        block.activityType !== 'Free'
      )
      .sort((a, b) => a.startTime.localeCompare(b.startTime));
  }

  getBlockTop(block: WeeklyAvailabilityBlock): number {
    const [hours, minutes] = block.startTime.split(':').map(Number);
    return ((hours * 60 + minutes) - 600) / 60 * 64;
  }

  getBlockHeight(block: WeeklyAvailabilityBlock): number {
    const [startHours, startMinutes] =
      block.startTime.split(':').map(Number);

    const [endHours, endMinutes] =
      block.endTime.split(':').map(Number);

    const duration =
      (endHours * 60 + endMinutes) -
      (startHours * 60 + startMinutes);

    return Math.max(duration / 60 * 64, 24);
  }

  getBlockStyle(
    block: { startTime: string; endTime: string }
  ): Record<string, string> {
    const top = this.getBlockTop(block as WeeklyAvailabilityBlock);
    const height = this.getBlockHeight(block as WeeklyAvailabilityBlock);

    return {
      top: `${top}px`,
      height: `${height}px`
    };
  }

  addBusyBlock(dayOfWeek: number, startHour = 10): void {
    if (startHour >= 20) {
      this.errorMessage = 'Choose a time within 10 AM–8 PM.';
      return;
    }

    const startTime = `${String(startHour).padStart(2, '0')}:00`;
    const endTime =
      `${String(Math.min(startHour + 1, 20)).padStart(2, '0')}:00`;

    this.weeklyBlocks.push({
      dayOfWeek,
      startTime,
      endTime,
      isFree: false,
      activityType: 'Meeting',
      title: ''
    });

    this.errorMessage = '';
    this.successMessage = '';
  }

  removeBlock(block: WeeklyAvailabilityBlock): void {
    this.weeklyBlocks = this.weeklyBlocks.filter(item => item !== block);
  }

  // -------------------------
  // Date-specific calendar events
  // -------------------------

  loadManualEvents(): void {
    const from = this.formatDateForApi(this.currentWeekStart);

    const weekEnd = new Date(this.currentWeekStart);
    weekEnd.setDate(weekEnd.getDate() + 6);

    const to = this.formatDateForApi(weekEnd);

    this.mentorCalendarEventService.getEvents(from, to).subscribe({
      next: events => {
        this.manualEvents = events;
      },
      error: (error: HttpErrorResponse) => {
        this.errorMessage =
          error.error?.message ||
          'Could not load date-specific calendar events.';
      }
    });
  }

  getEventsForDay(date: Date): MentorCalendarEvent[] {
    const dateString = this.formatDateForApi(date);

    return this.manualEvents
      .filter(event => event.date === dateString)
      .sort((a, b) => a.startTime.localeCompare(b.startTime));
  }

  openEventEditor(date: Date, startHour: number): void {
    if (startHour >= 20) {
      this.errorMessage = 'Choose a time within 10 AM–8 PM.';
      return;
    }

    this.selectedDate = this.formatDateForApi(date);
    this.eventStartTime = `${String(startHour).padStart(2, '0')}:00`;
    this.eventEndTime = `${String(startHour + 1).padStart(2, '0')}:00`;
    this.eventType = 'Meeting';
    this.eventTitle = '';
    this.studentName = '';
    this.isEventEditorOpen = true;

    this.errorMessage = '';
    this.successMessage = '';
  }

  saveManualEvent(): void {
    this.errorMessage = '';
    this.successMessage = '';

    if (!this.selectedDate) {
      this.errorMessage = 'Please select an event date.';
      return;
    }

    if (
      !this.eventStartTime ||
      !this.eventEndTime ||
      this.eventStartTime >= this.eventEndTime
    ) {
      this.errorMessage = 'Please enter a valid start and end time.';
      return;
    }

    if (
      this.eventStartTime < '10:00' ||
      this.eventEndTime > '20:00'
    ) {
      this.errorMessage =
        'Events must be within working hours (10 AM–8 PM).';
      return;
    }

    if (!this.eventTitle.trim()) {
      this.errorMessage = 'Please enter an event title or activity.';
      return;
    }

    if (!this.activityTypes.includes(this.eventType)) {
      this.errorMessage = 'Please select a valid activity type.';
      return;
    }

    if (
      this.eventType === 'Doubt Session' &&
      !this.studentName.trim()
    ) {
      this.errorMessage =
        'Please enter the student name for the doubt session.';
      return;
    }

    this.isSaving = true;

    this.mentorCalendarEventService.createEvent({
      date: this.selectedDate,
      startTime: this.eventStartTime,
      endTime: this.eventEndTime,
      activityType: this.eventType,
      title: this.eventTitle.trim(),
      studentName: this.eventType === 'Doubt Session'
        ? this.studentName.trim()
        : null
    }).subscribe({
      next: event => {
        this.isSaving = false;

        this.manualEvents = [...this.manualEvents, event]
          .sort((a, b) =>
            a.date.localeCompare(b.date) ||
            a.startTime.localeCompare(b.startTime)
          );

        this.isEventEditorOpen = false;
        this.successMessage = 'Calendar event saved successfully.';
      },
      error: (error: HttpErrorResponse) => {
        this.isSaving = false;
        this.errorMessage =
          error.error?.message ||
          'Could not save the calendar event. Please try again.';
      }
    });
  }

  deleteManualEvent(event: MentorCalendarEvent): void {
    this.errorMessage = '';
    this.successMessage = '';

    this.mentorCalendarEventService.deleteEvent(event.id).subscribe({
      next: () => {
        this.manualEvents = this.manualEvents.filter(
          item => item.id !== event.id
        );
        this.successMessage = 'Calendar event deleted successfully.';
      },
      error: (error: HttpErrorResponse) => {
        this.errorMessage =
          error.error?.message ||
          'Could not delete the calendar event.';
      }
    });
  }

  formatHour(hour: number): string {
    if (hour === 12) return '12 PM';
    if (hour > 12) return `${hour - 12} PM`;
    return `${hour} AM`;
  }
}
