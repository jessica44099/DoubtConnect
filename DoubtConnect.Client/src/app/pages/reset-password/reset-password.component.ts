
import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpClient } from '@angular/common/http';
import { ActivatedRoute } from '@angular/router';

@Component({
  selector: 'app-reset-password',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './reset-password.component.html',
  styleUrl: './reset-password.component.css'
})
export class ResetPasswordComponent {
  newPassword = '';
  confirmPassword = '';
  message = '';
  errorMessage = '';
  loading = false;

  private email = '';
  private token = '';

  constructor(
    private http: HttpClient,
    private route: ActivatedRoute
  ) {
    this.email = this.route.snapshot.queryParamMap.get('email') ?? '';
    this.token = this.route.snapshot.queryParamMap.get('token') ?? '';
  }

  resetPassword(): void {
    this.message = '';
    this.errorMessage = '';

    if (!this.email || !this.token) {
      this.errorMessage =
        'The reset link is incomplete. Please request a new password reset email.';
      return;
    }

    if (this.newPassword.length < 8) {
      this.errorMessage = 'Password must be at least 8 characters long.';
      return;
    }

    if (this.newPassword !== this.confirmPassword) {
      this.errorMessage = 'Passwords do not match.';
      return;
    }

    this.loading = true;

    this.http.post<{ message: string }>(
      'http://localhost:5234/api/Auth/reset-password',
      {
        email: this.email,
        token: this.token,
        newPassword: this.newPassword
      }
    ).subscribe({
      next: (response) => {
        this.message = response.message;
        this.loading = false;
      },
      error: (error) => {
        this.errorMessage =
          error.error?.message ??
          error.error?.errors?.join(' ') ??
          'Password reset failed. Please request a new reset link.';
        this.loading = false;
      }
    });
  }
}
